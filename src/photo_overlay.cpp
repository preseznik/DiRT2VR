#include "photo_overlay.h"
#include "overlay_blit.h"
#include "common.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <gdiplus.h>
#include <shlobj.h>
#include <atomic>
#include <algorithm>
#include <cstdio>
#include <cmath>
#include <cstring>
#include <cwchar>
#include <functional>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

namespace vr {
namespace {
using Microsoft::WRL::ComPtr;
constexpr ULONGLONG MessageMs=4000;
std::mutex messageMutex;
std::wstring message;
ULONGLONG messageTick{};
bool messageError{};
std::atomic<bool> saving{};

void SetMessage(std::wstring text,bool error) {
    std::lock_guard lock(messageMutex);
    message=std::move(text);messageTick=GetTickCount64();messageError=error;
}
bool StartGdiplus() {
    static const bool started=[] {
        ULONG_PTR token{};Gdiplus::GdiplusStartupInput input;
        return Gdiplus::GdiplusStartup(&token,&input,nullptr)==Gdiplus::Ok; // Kept for the process lifetime.
    }();
    return started;
}
bool PngEncoder(CLSID& clsid) {
    UINT count{},bytes{};
    if(Gdiplus::GetImageEncodersSize(&count,&bytes)!=Gdiplus::Ok || !bytes)return false;
    std::vector<BYTE> buffer(bytes);
    auto* codecs=reinterpret_cast<Gdiplus::ImageCodecInfo*>(buffer.data());
    if(Gdiplus::GetImageEncoders(count,bytes,codecs)!=Gdiplus::Ok)return false;
    for(UINT i=0;i<count;++i)if(!wcscmp(codecs[i].MimeType,L"image/png")){clsid=codecs[i].Clsid;return true;}
    return false;
}
std::wstring PhotoFolder() {
    PWSTR pictures{};
    if(FAILED(SHGetKnownFolderPath(FOLDERID_Pictures,KF_FLAG_CREATE,nullptr,&pictures)))return {};
    std::wstring folder=std::wstring(pictures)+L"\\DiRT2VR";
    CoTaskMemFree(pictures);
    if(!CreateDirectoryW(folder.c_str(),nullptr) && GetLastError()!=ERROR_ALREADY_EXISTS)return {};
    return folder;
}
// Runs on its own thread so PNG compression never stalls the game.
void Encode(std::vector<unsigned char> pixels,unsigned width,unsigned height) {
    SYSTEMTIME t{};GetLocalTime(&t);
    const auto name=PhotoFileName(t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond,t.wMilliseconds);
    bool saved=false;CLSID png{};
    const auto folder=PhotoFolder();
    if(!folder.empty() && StartGdiplus() && PngEncoder(png)) {
        Gdiplus::Bitmap bitmap(int(width),int(height),int(width*4),PixelFormat32bppRGB,pixels.data());
        saved=bitmap.GetLastStatus()==Gdiplus::Ok && bitmap.Save((folder+L"\\"+name).c_str(),&png,nullptr)==Gdiplus::Ok;
    }
    Log("photo capture: saved=%d size=%ux%u folder_ready=%d",saved,width,height,!folder.empty());
    SetMessage(saved?L"Photo saved: Pictures\\DiRT2VR\\"+name:L"The photo could not be saved.",!saved);
    saving=false;
}

// A GDI+ painted image copied onto the frame, redrawn only when its text changes.
// A GDI+ painted (premultiplied) image, redrawn only when its content changes and
// blended onto the frame, so the scene shows through.
class PanelImage {
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11Texture2D> texture_;
    OverlayBlit blit_;
    std::unique_ptr<Gdiplus::Bitmap> bitmap_;
    std::wstring last_;
    unsigned width_{},height_{};
public:
    bool Draw(ID3D11RenderTargetView* target,unsigned width,unsigned height,const std::wstring& signature,
              const std::function<void(Gdiplus::Graphics&)>& paint,float x,float y,float w,float h) {
        ComPtr<ID3D11Device> device;target->GetDevice(&device);
        if(!StartGdiplus())return false;
        if(device_.Get()!=device.Get() || width!=width_ || height!=height_) {
            device_=device;texture_.Reset();bitmap_.reset();last_.clear();width_=width;height_=height;
            D3D11_TEXTURE2D_DESC d{};d.Width=width;d.Height=height;d.MipLevels=d.ArraySize=1;
            d.Format=DXGI_FORMAT_B8G8R8A8_UNORM;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            if(FAILED(device->CreateTexture2D(&d,nullptr,&texture_)))return false;
            bitmap_=std::make_unique<Gdiplus::Bitmap>(int(width),int(height),PixelFormat32bppPARGB);
        }
        if(!texture_)return false;
        if(signature!=last_) {
            {
                Gdiplus::Graphics g(bitmap_.get());
                g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
                g.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAliasGridFit);
                g.Clear(Gdiplus::Color(0,0,0,0));
                paint(g);
            }
            Gdiplus::BitmapData data{};Gdiplus::Rect rect(0,0,int(width_),int(height_));
            if(bitmap_->LockBits(&rect,Gdiplus::ImageLockModeRead,PixelFormat32bppPARGB,&data)!=Gdiplus::Ok)return false;
            ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
            context->UpdateSubresource(texture_.Get(),0,nullptr,data.Scan0,UINT(data.Stride),0);
            bitmap_->UnlockBits(&data);
            last_=signature;
        }
        return blit_.Draw(target,texture_.Get(),x,y,w,h);
    }
};
PanelImage settingsPanel,hintBar,toastPanel;

void RoundedRect(Gdiplus::Graphics& g,const Gdiplus::Brush& brush,float x,float y,float w,float h,float r) {
    Gdiplus::GraphicsPath path;
    path.AddArc(x,y,2*r,2*r,180,90);path.AddArc(x+w-2*r,y,2*r,2*r,270,90);
    path.AddArc(x+w-2*r,y+h-2*r,2*r,2*r,0,90);path.AddArc(x,y+h-2*r,2*r,2*r,90,90);
    path.CloseFigure();g.FillPath(&brush,&path);
}

std::wstring KeyName(unsigned scan) {
    wchar_t name[32]{};
    return GetKeyNameTextW(LONG(scan<<16),name,32)>0?name:L"?";
}
}

std::wstring PhotoFileName(int year,int month,int day,int hour,int minute,int second,int millisecond) {
    wchar_t name[64]{};
    swprintf_s(name,L"DiRT2VR-%04d%02d%02d-%02d%02d%02d-%03d.png",year,month,day,hour,minute,second,millisecond);
    return name;
}

void PhotoCapture(IDXGISwapChain* swapchain) {
    if(saving.exchange(true)){SetMessage(L"Still saving the previous photo\u2026",true);return;}
    const auto fail=[](const char* why) {
        Log("photo capture failed: %s",why);SetMessage(L"The photo could not be saved.",true);saving=false;
    };
    ComPtr<ID3D11Texture2D> back;
    if(!swapchain || FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&back))))return fail("back buffer");
    ComPtr<ID3D11Device> device;back->GetDevice(&device);
    ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
    D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);
    const auto format=desc.Format;
    const bool rgba=format==DXGI_FORMAT_R8G8B8A8_UNORM || format==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    const bool bgra=format==DXGI_FORMAT_B8G8R8A8_UNORM || format==DXGI_FORMAT_B8G8R8A8_UNORM_SRGB ||
        format==DXGI_FORMAT_B8G8R8X8_UNORM || format==DXGI_FORMAT_B8G8R8X8_UNORM_SRGB;
    const bool tenBit=format==DXGI_FORMAT_R10G10B10A2_UNORM;
    if(!rgba && !bgra && !tenBit){Log("photo capture: unsupported format=%u",unsigned(format));return fail("format");}
    ComPtr<ID3D11Texture2D> source=back,staging;
    auto copy=desc;copy.SampleDesc={1,0};copy.BindFlags=0;copy.MiscFlags=0;copy.MipLevels=1;copy.ArraySize=1;
    if(desc.SampleDesc.Count>1) {
        copy.Usage=D3D11_USAGE_DEFAULT;copy.CPUAccessFlags=0;
        if(FAILED(device->CreateTexture2D(&copy,nullptr,&source)))return fail("resolve texture");
        context->ResolveSubresource(source.Get(),0,back.Get(),0,format);
    }
    copy.Usage=D3D11_USAGE_STAGING;copy.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    if(FAILED(device->CreateTexture2D(&copy,nullptr,&staging)))return fail("staging texture");
    context->CopySubresourceRegion(staging.Get(),0,0,0,0,source.Get(),0,nullptr);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if(FAILED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)))return fail("map");
    std::vector<unsigned char> pixels;
    try{pixels.resize(size_t(desc.Width)*desc.Height*4);}
    catch(const std::bad_alloc&){context->Unmap(staging.Get(),0);return fail("memory");}
    for(UINT y=0;y<desc.Height;++y) {
        const auto* in=static_cast<const unsigned char*>(mapped.pData)+size_t(y)*mapped.RowPitch;
        auto* out=pixels.data()+size_t(y)*desc.Width*4;
        for(UINT x=0;x<desc.Width;++x,in+=4,out+=4) {
            if(tenBit) {
                unsigned v{};std::memcpy(&v,in,4);
                const auto byte=[](unsigned c){return static_cast<unsigned char>((c*255+511)/1023);};
                out[0]=byte((v>>20)&1023);out[1]=byte((v>>10)&1023);out[2]=byte(v&1023);
            } else {
                out[0]=in[bgra?0:2];out[1]=in[1];out[2]=in[bgra?2:0];
            }
            out[3]=255; // The game's alpha channel is not picture transparency.
        }
    }
    context->Unmap(staging.Get(),0);
    SetMessage(L"Saving photo\u2026",false);
    try{std::thread(Encode,std::move(pixels),unsigned(desc.Width),unsigned(desc.Height)).detach();}
    catch(const std::exception&){fail("thread");}
}

void PhotoShowMessage(const std::wstring& text,bool error){SetMessage(text,error);}
void PhotoPanelDraw(IDXGISwapChain* swapchain,const PhotoPanelState& state) {
    std::wstring text;bool error{},recent{};
    {
        std::lock_guard lock(messageMutex);
        recent=!message.empty() && GetTickCount64()-messageTick<MessageMs;
        if(recent){text=message;error=messageError;}
    }
    if(!state.panel && !recent)return;
    ComPtr<ID3D11Texture2D> back;
    if(!swapchain || FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&back))))return;
    ComPtr<ID3D11Device> device;back->GetDevice(&device);
    D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);
    ComPtr<ID3D11RenderTargetView> target;
    if(FAILED(device->CreateRenderTargetView(back.Get(),nullptr,&target)))return;
    // Panel artwork is drawn for a 1800-pixel-high screen and scaled to the frame.
    const float scale=float(desc.Height)/1800.f,margin=float(desc.Height)/30.f;
    const Gdiplus::Color white(255,240,242,244),dim(255,120,126,132),grey(255,150,156,162),accent(255,183,235,25),orange(255,255,170,60);
    bool drawn=true;
    float hintTop=float(desc.Height);
    if(state.panel) {
        // Settings: one row per option, grouped, the selected row highlighted.
        constexpr int width=760,header=92,rowHeight=54,sectionHeight=50,bottom=22;
        int height=header+bottom;
        std::wstring signature=state.title+L"|"+std::to_wstring(state.selected);
        for(size_t i=0;i<state.rows.size();++i) {
            const auto& row=state.rows[i];
            if(!row.section.empty())height+=sectionHeight;
            height+=rowHeight;
            signature+=row.section+L"|"+row.label+L"|"+row.value+L"|"+row.key+(row.dim?L"d":L"")+(row.warn?L"w":L"");
        }
        drawn&=settingsPanel.Draw(target.Get(),width,unsigned(height),signature,[&](Gdiplus::Graphics& g) {
            Gdiplus::SolidBrush background(Gdiplus::Color(210,14,16,18)),highlight(Gdiplus::Color(56,255,255,255));
            Gdiplus::SolidBrush whiteBrush(white),dimBrush(dim),greyBrush(grey),accentBrush(accent),orangeBrush(orange);
            RoundedRect(g,background,0,0,float(width),float(height),22);
            Gdiplus::FontFamily family(L"Segoe UI");
            Gdiplus::Font title(&family,34,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),section(&family,21,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),
                label(&family,28,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel),value(&family,28,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),
                key(&family,20,Gdiplus::FontStyleBold,Gdiplus::UnitPixel);
            Gdiplus::StringFormat right;right.SetAlignment(Gdiplus::StringAlignmentFar);right.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            right.SetFormatFlags(Gdiplus::StringFormatFlagsNoWrap);right.SetTrimming(Gdiplus::StringTrimmingEllipsisCharacter); // Long filter names.
            Gdiplus::StringFormat left;left.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            Gdiplus::StringFormat centre;centre.SetAlignment(Gdiplus::StringAlignmentCenter);centre.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            g.FillRectangle(&accentBrush,32.f,34.f,8.f,30.f);
            g.DrawString(state.title.c_str(),-1,&title,Gdiplus::RectF(52,22,600,54),&left,&whiteBrush);
            float y=float(header);
            for(size_t i=0;i<state.rows.size();++i) {
                const auto& row=state.rows[i];
                if(!row.section.empty()) {
                    g.DrawString(row.section.c_str(),-1,&section,Gdiplus::RectF(32,y+8,width-64.f,sectionHeight-8.f),&left,&greyBrush);
                    y+=sectionHeight;
                }
                const bool selected=int(i)==state.selected;
                if(selected) {
                    RoundedRect(g,highlight,14,y+2,width-28.f,rowHeight-4.f,10);
                    g.FillRectangle(&accentBrush,14.f,y+10,6.f,rowHeight-20.f);
                }
                const auto& labelBrush=row.dim?dimBrush:whiteBrush;
                const auto& valueBrush=row.warn?orangeBrush:row.dim?dimBrush:selected?accentBrush:whiteBrush;
                g.DrawString(row.label.c_str(),-1,&label,Gdiplus::RectF(36,y,360,float(rowHeight)),&left,&labelBrush);
                const float keyWidth=row.key.empty()?0.f:66.f;
                const Gdiplus::RectF valueBox(410,y,width-450.f-keyWidth,float(rowHeight));
                if(selected && !row.dim) {
                    // The left arrow sits just before the value.
                    Gdiplus::RectF size;g.MeasureString(row.value.c_str(),-1,&value,Gdiplus::PointF(0,0),&size);
                    g.DrawString(L"\u2039",-1,&value,Gdiplus::RectF(std::max(valueBox.X-30,valueBox.GetRight()-size.Width-34),y,30,float(rowHeight)),&centre,&accentBrush);
                }
                g.DrawString(row.value.c_str(),-1,&value,valueBox,&right,&valueBrush);
                if(selected && !row.dim)
                    g.DrawString(L"\u203a",-1,&value,Gdiplus::RectF(width-44.f-keyWidth,y,30,float(rowHeight)),&centre,&accentBrush);
                if(!row.key.empty()) {
                    Gdiplus::SolidBrush cap(Gdiplus::Color(60,255,255,255));
                    RoundedRect(g,cap,width-30.f-keyWidth+8,y+12,keyWidth-8,rowHeight-24.f,6);
                    g.DrawString(row.key.c_str(),-1,&key,Gdiplus::RectF(width-30.f-keyWidth+8,y,keyWidth-8,float(rowHeight)),&centre,&greyBrush);
                }
                y+=rowHeight;
            }
        },float(desc.Width)-margin-width*scale,margin,width*scale,height*scale);
        // Hint bar: only the essentials; everything else is in the panel.
        const std::wstring hints=!state.hints.empty()?state.hints:state.controller?
            L"LS  Move     RS  Look     RT / LT  Up / Down     D-pad  Select / Change     A  Photo     View  Hide     B  Back":
            L"WASD  Move     Mouse  Look     E / Q  Up / Down     \u2191\u2193  Select     \u2190\u2192  Change     P  Photo     H  Hide     Esc  Back";
        constexpr int hintWidth=1800,hintHeight=64;
        const float w=std::min(hintWidth*scale,float(desc.Width)-2*margin),h=w*hintHeight/hintWidth;
        hintTop=float(desc.Height)-margin-h;
        drawn&=hintBar.Draw(target.Get(),hintWidth,hintHeight,hints,[&](Gdiplus::Graphics& g) {
            Gdiplus::SolidBrush background(Gdiplus::Color(190,14,16,18)),whiteBrush(white);
            RoundedRect(g,background,0,0,float(hintWidth),float(hintHeight),hintHeight/2.f);
            Gdiplus::Font font(L"Segoe UI",25,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
            Gdiplus::StringFormat centre;centre.SetAlignment(Gdiplus::StringAlignmentCenter);centre.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            g.DrawString(hints.c_str(),-1,&font,Gdiplus::RectF(0,0,float(hintWidth),float(hintHeight)),&centre,&whiteBrush);
        },(float(desc.Width)-w)/2,hintTop,w,h);
    }
    if(recent) {
        // Messages (photo saved) above the hint bar, centred.
        constexpr int toastWidth=1300,toastHeight=70;
        const float w=std::min(toastWidth*scale,float(desc.Width)-2*margin),h=w*toastHeight/toastWidth;
        drawn&=toastPanel.Draw(target.Get(),toastWidth,toastHeight,text+(error?L"!":L""),[&](Gdiplus::Graphics& g) {
            Gdiplus::SolidBrush background(Gdiplus::Color(210,14,16,18)),brush(error?orange:accent);
            RoundedRect(g,background,0,0,float(toastWidth),float(toastHeight),18);
            Gdiplus::Font font(L"Segoe UI",27,Gdiplus::FontStyleBold,Gdiplus::UnitPixel);
            Gdiplus::StringFormat centre;centre.SetAlignment(Gdiplus::StringAlignmentCenter);centre.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            g.DrawString(text.c_str(),-1,&font,Gdiplus::RectF(0,0,float(toastWidth),float(toastHeight)),&centre,&brush);
        },(float(desc.Width)-w)/2,(state.panel?hintTop:float(desc.Height)-margin)-h*1.4f,w,h);
    }
    static bool reported{};
    if(!drawn && !reported){reported=true;Log("photo panel: draw failed");}
}
}
