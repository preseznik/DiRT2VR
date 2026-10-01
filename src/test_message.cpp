#include "test_message.h"
#include "common.h"
#include "camera_math.h"
#include "eye_blit.h"
#include <gdiplus.h>
#include <algorithm>
#include <array>
#include <stdexcept>

using Microsoft::WRL::ComPtr;
namespace vr {
namespace {
std::wstring title,body;
uint64_t revision{},presentedAt{},visibleSince{},expiresAt{},lastPoll{};
unsigned lifetime=120;
bool drawn{},failed{};
XrFrames::Overlay notice;
XrPosef messageReference{{0,0,0,1},{0,0,0}};
std::wstring Decode(const std::string& bytes) {
    const int size=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,bytes.data(),int(bytes.size()),nullptr,0);
    if(size<=0)return {};
    std::wstring text(size,L'\0');
    MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,bytes.data(),int(bytes.size()),text.data(),size);
    if(text.front()==0xfeff)text.erase(0,1);
    text.erase(std::remove(text.begin(),text.end(),L'\r'),text.end());
    for(auto c:text)if(c<32 && c!=L'\n')return {};
    return text;
}
void Status(const char* state) {
    auto out=TraceFile(Output()/"headset-message-status.txt");
    out<<"revision="<<revision<<"\nstate="<<state<<'\n';
    Log("headset test message revision=%llu state=%s",revision,state);
}
class MessageImage {
    ComPtr<ID3D11Texture2D> texture_;
    EyeBlit blit_;
    ULONG_PTR gdiplus_{};
    uint64_t painted_{};
public:
    void Draw(ID3D11RenderTargetView* target,unsigned w,unsigned h) {
        ComPtr<ID3D11Device> device;target->GetDevice(&device);
        if(!texture_) {
            Gdiplus::GdiplusStartupInput startup;
            if(!gdiplus_ && Gdiplus::GdiplusStartup(&gdiplus_,&startup,nullptr)!=Gdiplus::Ok)throw std::runtime_error("test message text initialization");
            if(!blit_.Initialize(device.Get()))throw std::runtime_error("test message blit initialization");
            D3D11_TEXTURE2D_DESC d{};d.Width=768;d.Height=1024;d.MipLevels=d.ArraySize=1;
            d.Format=DXGI_FORMAT_B8G8R8A8_UNORM;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            if(FAILED(device->CreateTexture2D(&d,nullptr,&texture_)))throw std::runtime_error("test message texture");
        }
        if(painted_!=revision) {
            Gdiplus::Bitmap bitmap(768,1024,PixelFormat32bppPARGB);
            {
                Gdiplus::Graphics g(&bitmap);g.Clear(Gdiplus::Color(0,0,0,0));
                g.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAliasGridFit);
                Gdiplus::SolidBrush background(Gdiplus::Color(246,23,27,29)),white(Gdiplus::Color(255,246,246,246)),lime(Gdiplus::Color(255,185,235,45));
                g.FillRectangle(&background,12,310,744,400);g.FillRectangle(&lime,12,310,6,400);
                Gdiplus::Font label(L"Segoe UI",23,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),heading(L"Segoe UI",38,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),text(L"Segoe UI",33,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
                g.DrawString(L"DiRT2VR  /  TEST MESSAGE",-1,&label,Gdiplus::RectF(42,332,680,38),nullptr,&lime);
                g.DrawString(title.c_str(),-1,&heading,Gdiplus::RectF(42,376,680,100),nullptr,&white);
                g.DrawString(body.c_str(),-1,&text,Gdiplus::RectF(42,486,680,205),nullptr,&white);
            }
            Gdiplus::BitmapData data{};Gdiplus::Rect rect(0,0,768,1024);
            if(bitmap.LockBits(&rect,Gdiplus::ImageLockModeRead,PixelFormat32bppPARGB,&data)!=Gdiplus::Ok)throw std::runtime_error("test message bitmap");
            ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
            context->UpdateSubresource(texture_.Get(),0,nullptr,data.Scan0,UINT(data.Stride),0);bitmap.UnlockBits(&data);
            painted_=revision;
        }
        if(!blit_.Draw(0,texture_.Get(),target,w,h,true))throw std::runtime_error("test message copy");
        drawn=true;
    }
};
MessageImage image;
}
bool TestMessagesEnabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_TEST_MESSAGES",value,8)==1 && value[0]==L'1';}();
    return enabled && LoggingEnabled();
}
uint64_t TestMessage(const std::wstring& heading,const std::wstring& text,unsigned seconds) {
    if(!TestMessagesEnabled())return 0;
    title=heading.substr(0,56);body=text.substr(0,180);++revision;presentedAt=visibleSince=0;
    lifetime=std::clamp(seconds,1u,300u);expiresAt=GetTickCount64()+300000;failed=false;
    Status(title.empty()?"cleared":"queued");return revision;
}
bool TestMessageCurrent(uint64_t value){return value && value==revision && !title.empty();}
bool TestMessageVisibleFor(uint64_t value,uint64_t milliseconds){return TestMessageCurrent(value) && visibleSince && GetTickCount64()-visibleSince>=milliseconds;}
void PollTestMessage() {
    if(!TestMessagesEnabled())return;
    auto now=GetTickCount64();if(now-lastPoll<250)return;lastPoll=now;
    const auto file=Output()/"headset-message.txt";
    std::ifstream input(file,std::ios::binary);if(!input)return;
    std::array<char,2049> bytes{};input.read(bytes.data(),bytes.size());const auto size=input.gcount();input.close();
    std::error_code error;std::filesystem::remove(file,error);if(error)return;
    if(size>2048){Status("rejected-too-long");return;}
    if(!size){TestMessage(L"",L"");return;}
    const auto text=Decode(std::string(bytes.data(),size));const auto split=text.find(L'\n');
    if(split==std::wstring::npos || !split || split>56 || text.size()-split-1>180){Status("rejected-format");return;}
    TestMessage(text.substr(0,split),text.substr(split+1));
}
XrFrames::Overlay* TestMessageOverlay(){return TestMessagesEnabled()?&notice:nullptr;}
void SetTestMessageReference(const XrPosef& reference) { messageReference=reference; }
void PrepareTestMessage(bool seatPanelActive) {
    drawn=false;notice.enabled=false;
    if(!TestMessagesEnabled() || title.empty() || failed || seatPanelActive)return;
    if(GetTickCount64()>=expiresAt){title.clear();Status("expired");return;}
    // Anchor below the recentered forward view, like the car-relative HUD.
    // Head turns/leans and message revisions must not pull the card over the road.
    notice.pose=ScreenPose(messageReference,1.6f);notice.pose.position.y-=.65f;notice.size={.9f,1.2f};
    notice.draw=[](unsigned,const XrView&,ID3D11RenderTargetView* target,uint32_t w,uint32_t h){image.Draw(target,w,h);};
    notice.unavailable=[] {failed=true;Status("unavailable");};notice.enabled=true;
}
void TestMessageSubmitted(bool submitted) {
    if(!submitted || !drawn){visibleSince=0;return;}
    if(!visibleSince)visibleSince=GetTickCount64();
    if(!presentedAt){presentedAt=GetTickCount64();expiresAt=presentedAt+lifetime*1000ull;Status("submitted");}
}
}
