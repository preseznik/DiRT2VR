#include "photo_overlay.h"
#include "eye_blit.h"
#include "common.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <gdiplus.h>
#include <shlobj.h>
#include <atomic>
#include <cstdio>
#include <cstring>
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
class PanelImage {
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11Texture2D> texture_;
    EyeBlit blit_;
    std::unique_ptr<Gdiplus::Bitmap> bitmap_;
    std::wstring last_;
    unsigned width_,height_;
public:
    PanelImage(unsigned width,unsigned height):width_(width),height_(height) {}
    float Aspect() const {return float(height_)/float(width_);}
    bool Draw(ID3D11RenderTargetView* target,const std::wstring& signature,const std::function<void(Gdiplus::Graphics&)>& paint,
              unsigned w,unsigned h,float x,float y) {
        ComPtr<ID3D11Device> device;target->GetDevice(&device);
        if(device_.Get()!=device.Get()) {
            device_=device;texture_.Reset();blit_=EyeBlit{};last_.clear();
            if(!StartGdiplus() || !blit_.Initialize(device.Get()))return false;
            D3D11_TEXTURE2D_DESC d{};d.Width=width_;d.Height=height_;d.MipLevels=d.ArraySize=1;
            d.Format=DXGI_FORMAT_B8G8R8A8_UNORM;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            if(FAILED(device->CreateTexture2D(&d,nullptr,&texture_)))return false;
        }
        if(!texture_)return false;
        if(signature!=last_) {
            if(!bitmap_)bitmap_=std::make_unique<Gdiplus::Bitmap>(int(width_),int(height_),PixelFormat32bppPARGB);
            {
                Gdiplus::Graphics g(bitmap_.get());
                g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
                g.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAliasGridFit);
                g.Clear(Gdiplus::Color(235,25,27,29));
                paint(g);
            }
            Gdiplus::BitmapData data{};Gdiplus::Rect rect(0,0,int(width_),int(height_));
            if(bitmap_->LockBits(&rect,Gdiplus::ImageLockModeRead,PixelFormat32bppPARGB,&data)!=Gdiplus::Ok)return false;
            ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
            context->UpdateSubresource(texture_.Get(),0,nullptr,data.Scan0,UINT(data.Stride),0);
            bitmap_->UnlockBits(&data);
            last_=signature;
        }
        return blit_.Draw(0,texture_.Get(),target,w,h,true,x,y);
    }
};
PanelImage fullPanel(960,440),toastPanel(960,80);

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
    const Gdiplus::Color white(255,245,245,245),green(255,183,235,25),grey(255,160,164,168),orange(255,255,170,60);
    const unsigned height=desc.Height*27/100,width=unsigned(float(height)/fullPanel.Aspect());
    const float margin=float(desc.Height/40);
    static bool reported{};
    bool drawn{};
    if(state.panel) {
        wchar_t status[96]{};
        swprintf_s(status,L"View %.0f\u00b0    Tilt %+.0f\u00b0    Speed %s",state.viewDegrees,state.tiltDegrees,
            state.speed<0?L"slower":state.speed>0?L"faster":L"normal");
        const auto tilt=KeyName(0x2c)+L"/"+KeyName(0x2e);
        const std::wstring keyboard[]={L"WASD move  \u00b7  Mouse look  \u00b7  E/Q up/down",L"Shift faster  \u00b7  Ctrl slower",
            L"Wheel or PgUp/PgDn zoom  \u00b7  "+tilt+L" tilt",L"Middle button: clear zoom and tilt",
            L"R start view  \u00b7  P save photo",L"H hide panel  \u00b7  Esc back to the pause menu"};
        const std::wstring pad[]={L"Left stick move  \u00b7  Right stick look  \u00b7  RT/LT up/down",L"RB faster  \u00b7  LB slower",
            L"D-pad up/down zoom  \u00b7  left/right tilt",L"Right-stick click: clear zoom and tilt",
            L"Y start view  \u00b7  A save photo",L"View button: hide panel  \u00b7  B back to the pause menu"};
        const auto& help=state.controller?pad:keyboard;
        const std::wstring footer=recent?text:L"Photos are saved to Pictures\\DiRT2VR";
        std::wstring signature=std::wstring(status)+footer+(error?L"!":L"");
        for(const auto& line:help)signature+=line;
        drawn=fullPanel.Draw(target.Get(),signature,[&](Gdiplus::Graphics& g) {
            Gdiplus::Font title(L"Segoe UI",44,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),bold(L"Segoe UI",30,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),
                normal(L"Segoe UI",28,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
            Gdiplus::SolidBrush whiteBrush(white),greenBrush(green),greyBrush(grey),orangeBrush(orange);
            g.DrawString(L"PHOTO MODE",-1,&title,Gdiplus::PointF(32,18),&whiteBrush);
            g.DrawString(status,-1,&bold,Gdiplus::PointF(34,80),&greenBrush);
            float y=130;
            for(const auto& line:help){g.DrawString(line.c_str(),-1,&normal,Gdiplus::PointF(34,y),&whiteBrush);y+=40;}
            g.DrawString(footer.c_str(),-1,&normal,Gdiplus::PointF(34,y+8),recent?(error?&orangeBrush:&greenBrush):&greyBrush);
        },width,height,margin,margin);
    } else {
        drawn=toastPanel.Draw(target.Get(),text+(error?L"!":L""),[&](Gdiplus::Graphics& g) {
            Gdiplus::Font normal(L"Segoe UI",30,Gdiplus::FontStyleBold,Gdiplus::UnitPixel);
            Gdiplus::SolidBrush brush(error?orange:green);
            g.DrawString(text.c_str(),-1,&normal,Gdiplus::PointF(34,20),&brush);
        },width,unsigned(float(width)*toastPanel.Aspect()),margin,margin);
    }
    if(!drawn && !reported){reported=true;Log("photo panel: draw failed");}
}
}
