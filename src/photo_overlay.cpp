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
void Encode(std::vector<unsigned char> pixels,unsigned width,unsigned height,bool stereo,PhotoStereoView view) {
    SYSTEMTIME t{};GetLocalTime(&t);
    auto name=PhotoFileName(t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond,t.wMilliseconds);
    // Side-by-side stereo (left eye on the left), marked in the name for viewers and the launcher.
    if(stereo)name.insert(name.size()-4,L"-SBS");
    bool saved=false;CLSID png{};
    const auto folder=PhotoFolder();
    if(!folder.empty() && StartGdiplus() && PngEncoder(png)) {
        Gdiplus::Bitmap bitmap(int(width),int(height),int(width*4),PixelFormat32bppRGB,pixels.data());
        saved=bitmap.GetLastStatus()==Gdiplus::Ok && bitmap.Save((folder+L"\\"+name).c_str(),&png,nullptr)==Gdiplus::Ok;
    }
    if(saved && stereo && view.valid) {
        // The view beside the photo: the 3D viewer shows each eye with the field of view it was taken with.
        char json[512]{};
        std::snprintf(json,sizeof json,"{\"format\":\"dirt2vr-sbs\",\"version\":1,\"eyeDistance\":%.5f,"
            "\"left\":[%.6f,%.6f,%.6f,%.6f],\"right\":[%.6f,%.6f,%.6f,%.6f]}\n",view.eyeDistance,
            view.tangents[0][0],view.tangents[0][1],view.tangents[0][2],view.tangents[0][3],
            view.tangents[1][0],view.tangents[1][1],view.tangents[1][2],view.tangents[1][3]);
        auto sidecar=folder+L"\\"+name;sidecar.replace(sidecar.size()-4,4,L".json");
        const HANDLE file=CreateFileW(sidecar.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file!=INVALID_HANDLE_VALUE){DWORD written{};WriteFile(file,json,DWORD(std::strlen(json)),&written,nullptr);CloseHandle(file);}
    }
    Log("photo capture: saved=%d size=%ux%u stereo=%d folder_ready=%d",saved,width,height,stereo,!folder.empty());
    if(stereo)MessageBeep(saved?MB_OK:MB_ICONHAND); // No panel in the headset: a sound confirms the save.
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
    explicit PanelImage(bool coverage=false):blit_(coverage) {}
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
// VR: the same artwork on the headset's transparent layer, keeping its alpha.
PanelImage headsetPanel{true},headsetHints{true},headsetToast{true};

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

namespace {
// The texture's picture as 32-bit BGRX rows; false with the reason in why.
bool ReadPixels(ID3D11Texture2D* texture,std::vector<unsigned char>& pixels,UINT& width,UINT& height,const char*& why) {
    ComPtr<ID3D11Device> device;texture->GetDevice(&device);
    ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
    D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);
    const auto format=desc.Format;
    const bool rgba=format==DXGI_FORMAT_R8G8B8A8_UNORM || format==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    const bool bgra=format==DXGI_FORMAT_B8G8R8A8_UNORM || format==DXGI_FORMAT_B8G8R8A8_UNORM_SRGB ||
        format==DXGI_FORMAT_B8G8R8X8_UNORM || format==DXGI_FORMAT_B8G8R8X8_UNORM_SRGB;
    const bool tenBit=format==DXGI_FORMAT_R10G10B10A2_UNORM;
    if(!rgba && !bgra && !tenBit){Log("photo capture: unsupported format=%u",unsigned(format));why="format";return false;}
    ComPtr<ID3D11Texture2D> source=texture,staging;
    auto copy=desc;copy.SampleDesc={1,0};copy.BindFlags=0;copy.MiscFlags=0;copy.MipLevels=1;copy.ArraySize=1;
    if(desc.SampleDesc.Count>1) {
        copy.Usage=D3D11_USAGE_DEFAULT;copy.CPUAccessFlags=0;
        if(FAILED(device->CreateTexture2D(&copy,nullptr,&source))){why="resolve texture";return false;}
        context->ResolveSubresource(source.Get(),0,texture,0,format);
    }
    copy.Usage=D3D11_USAGE_STAGING;copy.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    if(FAILED(device->CreateTexture2D(&copy,nullptr,&staging))){why="staging texture";return false;}
    context->CopySubresourceRegion(staging.Get(),0,0,0,0,source.Get(),0,nullptr);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if(FAILED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped))){why="map";return false;}
    try{pixels.resize(size_t(desc.Width)*desc.Height*4);}
    catch(const std::bad_alloc&){context->Unmap(staging.Get(),0);why="memory";return false;}
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
    width=desc.Width;height=desc.Height;return true;
}
void StartEncode(std::vector<unsigned char> pixels,unsigned width,unsigned height,bool stereo,const PhotoStereoView& view={}) {
    SetMessage(L"Saving photo\u2026",false);
    try{std::thread(Encode,std::move(pixels),width,height,stereo,view).detach();}
    catch(const std::exception&){Log("photo capture failed: thread");SetMessage(L"The photo could not be saved.",true);saving=false;}
}
}

void PhotoCapture(IDXGISwapChain* swapchain) {
    if(saving.exchange(true)){SetMessage(L"Still saving the previous photo\u2026",true);return;}
    const auto fail=[](const char* why) {
        Log("photo capture failed: %s",why);SetMessage(L"The photo could not be saved.",true);saving=false;
    };
    ComPtr<ID3D11Texture2D> back;
    if(!swapchain || FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&back))))return fail("back buffer");
    std::vector<unsigned char> pixels;UINT width{},height{};const char* why="";
    if(!ReadPixels(back.Get(),pixels,width,height,why))return fail(why);
    StartEncode(std::move(pixels),width,height,false);
}

void PhotoCaptureStereo(ID3D11Texture2D* left,ID3D11Texture2D* right,const PhotoStereoView& view) {
    if(!left || !right)return;
    if(saving.exchange(true)){Log("photo capture: still saving");MessageBeep(MB_ICONHAND);return;}
    const auto fail=[](const char* why){Log("photo capture failed: %s",why);MessageBeep(MB_ICONHAND);saving=false;};
    std::vector<unsigned char> a,b;UINT wa{},ha{},wb{},hb{};const char* why="";
    if(!ReadPixels(left,a,wa,ha,why) || !ReadPixels(right,b,wb,hb,why))return fail(why);
    if(ha!=hb)return fail("eye sizes");
    // Left eye on the left, right eye on the right (parallel viewing).
    std::vector<unsigned char> pixels;
    try{pixels.resize(size_t(wa+wb)*ha*4);}catch(const std::bad_alloc&){return fail("memory");}
    for(UINT y=0;y<ha;++y) {
        std::memcpy(pixels.data()+size_t(y)*(wa+wb)*4,a.data()+size_t(y)*wa*4,size_t(wa)*4);
        std::memcpy(pixels.data()+(size_t(y)*(wa+wb)+wa)*4,b.data()+size_t(y)*wb*4,size_t(wb)*4);
    }
    StartEncode(std::move(pixels),wa+wb,ha,true,view);
}
void PhotoShowMessage(const std::wstring& text,bool error){SetMessage(text,error);}
namespace {
// The latest message, if it is still shown.
bool CurrentMessage(std::wstring& text,bool& error) {
    std::lock_guard lock(messageMutex);
    if(message.empty() || GetTickCount64()-messageTick>=MessageMs)return false;
    text=message;error=messageError;return true;
}
// Panel, hint bar and message on a target of this size (desktop frame or the headset layer).
bool DrawOverlay(ID3D11RenderTargetView* target,float frameWidth,float frameHeight,const PhotoPanelState& state,
                 bool recent,const std::wstring& text,bool error,bool headset) {
    // Panel artwork is drawn for a 1800-pixel-high screen and scaled to the frame. In the
    // headset it is larger and further from the edges, which are hard to read there.
    const float scale=frameHeight/(headset?1400.f:1800.f),margin=frameHeight/(headset?14.f:30.f);
    auto& settingsPanel=headset?headsetPanel:vr::settingsPanel;auto& hintBar=headset?headsetHints:vr::hintBar;
    auto& toastPanel=headset?headsetToast:vr::toastPanel;
    const Gdiplus::Color white(255,240,242,244),dim(255,120,126,132),grey(255,150,156,162),accent(255,183,235,25),orange(255,255,170,60);
    bool drawn=true;
    float hintTop=frameHeight;
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
        drawn&=settingsPanel.Draw(target,width,unsigned(height),signature,[&](Gdiplus::Graphics& g) {
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
        },frameWidth-margin-width*scale,margin,width*scale,height*scale);
        // Hint bar: only the essentials; everything else is in the panel.
        const std::wstring hints=!state.hints.empty()?state.hints:state.controller?
            L"LS  Move     RS  Look     RT / LT  Up / Down     D-pad  Select / Change     A  Photo     View  Hide     B  Back":
            L"WASD  Move     Mouse  Look     E / Q  Up / Down     \u2191\u2193  Select     \u2190\u2192  Change     P  Photo     H  Hide     Esc  Back";
        constexpr int hintWidth=1800,hintHeight=64;
        const float w=std::min(hintWidth*scale,frameWidth-2*margin),h=w*hintHeight/hintWidth;
        hintTop=frameHeight-margin-h;
        drawn&=hintBar.Draw(target,hintWidth,hintHeight,hints,[&](Gdiplus::Graphics& g) {
            Gdiplus::SolidBrush background(Gdiplus::Color(190,14,16,18)),whiteBrush(white);
            RoundedRect(g,background,0,0,float(hintWidth),float(hintHeight),hintHeight/2.f);
            Gdiplus::Font font(L"Segoe UI",25,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
            Gdiplus::StringFormat centre;centre.SetAlignment(Gdiplus::StringAlignmentCenter);centre.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            g.DrawString(hints.c_str(),-1,&font,Gdiplus::RectF(0,0,float(hintWidth),float(hintHeight)),&centre,&whiteBrush);
        },(frameWidth-w)/2,hintTop,w,h);
    }
    if(recent) {
        // Messages (photo saved) above the hint bar, centred.
        constexpr int toastWidth=1300,toastHeight=70;
        const float w=std::min(toastWidth*scale,frameWidth-2*margin),h=w*toastHeight/toastWidth;
        drawn&=toastPanel.Draw(target,toastWidth,toastHeight,text+(error?L"!":L""),[&](Gdiplus::Graphics& g) {
            Gdiplus::SolidBrush background(Gdiplus::Color(210,14,16,18)),brush(error?orange:accent);
            RoundedRect(g,background,0,0,float(toastWidth),float(toastHeight),18);
            Gdiplus::Font font(L"Segoe UI",27,Gdiplus::FontStyleBold,Gdiplus::UnitPixel);
            Gdiplus::StringFormat centre;centre.SetAlignment(Gdiplus::StringAlignmentCenter);centre.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            g.DrawString(text.c_str(),-1,&font,Gdiplus::RectF(0,0,float(toastWidth),float(toastHeight)),&centre,&brush);
        },(frameWidth-w)/2,(state.panel?hintTop:frameHeight-margin)-h*1.4f,w,h);
    }
    return drawn;
}
std::mutex headsetMutex;
PhotoPanelState headsetState;
bool headsetPanelShown{};
ComPtr<ID3D11Texture2D> headsetImage;
ComPtr<ID3D11RenderTargetView> headsetTarget;
ID3D11Texture2D* messageOwner{};
ComPtr<ID3D11RenderTargetView> messageTarget;
void ReportFailure(bool drawn) {
    static bool reported{};
    if(!drawn && !reported){reported=true;Log("photo panel: draw failed");}
}
}

void PhotoPanelDraw(IDXGISwapChain* swapchain,const PhotoPanelState& state) {
    std::wstring text;bool error{};
    const bool recent=CurrentMessage(text,error);
    if(!state.panel && !recent)return;
    ComPtr<ID3D11Texture2D> back;
    if(!swapchain || FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&back))))return;
    ComPtr<ID3D11Device> device;back->GetDevice(&device);
    D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);
    ComPtr<ID3D11RenderTargetView> target;
    if(FAILED(device->CreateRenderTargetView(back.Get(),nullptr,&target)))return;
    ReportFailure(DrawOverlay(target.Get(),float(desc.Width),float(desc.Height),state,recent,text,error,false));
}

void PhotoHeadsetState(const PhotoPanelState& state,bool active) {
    std::lock_guard lock(headsetMutex);headsetState=state;headsetPanelShown=active;
}
bool PhotoMessageShown() {
    std::wstring text;bool error{};return CurrentMessage(text,error);
}
ID3D11Texture2D* PhotoHeadsetImage(ID3D11Device* device,unsigned width,unsigned height,bool panel) {
    PhotoPanelState state;
    if(panel){std::lock_guard lock(headsetMutex);if(headsetPanelShown)state=headsetState;}
    std::wstring text;bool error{};
    const bool recent=CurrentMessage(text,error);
    if(!state.panel && !recent)return nullptr;
    D3D11_TEXTURE2D_DESC desc{};
    if(headsetImage)headsetImage->GetDesc(&desc);
    if(!headsetImage || desc.Width!=width || desc.Height!=height) {
        headsetImage.Reset();headsetTarget.Reset();
        desc={};desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=1;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        if(FAILED(device->CreateTexture2D(&desc,nullptr,&headsetImage)) ||
           FAILED(device->CreateRenderTargetView(headsetImage.Get(),nullptr,&headsetTarget))){headsetImage.Reset();ReportFailure(false);return nullptr;}
    }
    ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
    const float clear[4]{};context->ClearRenderTargetView(headsetTarget.Get(),clear);
    const bool drawn=DrawOverlay(headsetTarget.Get(),float(width),float(height),state,recent,text,error,true);ReportFailure(drawn);
    static unsigned reported{};const unsigned kind=1+state.panel*2+recent*4;
    if(reported!=kind){reported=kind;Log("photo headset layer: %ux%u panel=%d rows=%zu message=%d drawn=%d",width,height,state.panel,state.rows.size(),recent,drawn);}
    return headsetImage.Get();
}
void PhotoMessageOver(ID3D11Texture2D* hud) {
    std::wstring text;bool error{};
    if(!hud || !CurrentMessage(text,error))return;
    if(messageOwner!=hud) {
        messageTarget.Reset();messageOwner=nullptr;
        ComPtr<ID3D11Device> device;hud->GetDevice(&device);
        if(FAILED(device->CreateRenderTargetView(hud,nullptr,&messageTarget)))return;
        messageOwner=hud;
    }
    D3D11_TEXTURE2D_DESC desc{};hud->GetDesc(&desc);
    ReportFailure(DrawOverlay(messageTarget.Get(),float(desc.Width),float(desc.Height),PhotoPanelState{},true,text,error,true));
}
}
