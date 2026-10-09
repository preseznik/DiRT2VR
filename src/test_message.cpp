#include "test_message.h"
#include "vr_hotkeys.h"
#include "common.h"
#include "camera_math.h"
#include "eye_blit.h"
#include "message_reply.h"
#include "seat_adjustment.h"
#include <gdiplus.h>
#include <algorithm>
#include <array>
#include <stdexcept>
#include <mutex>

using Microsoft::WRL::ComPtr;
namespace vr {
namespace {
std::wstring title,body;
std::mutex messageMutex;
MessageReply reply;
std::string questionId;
uint64_t paintVersion{};
bool inputReady{},acceptInput{},question{},answered{},keysBlocked[256]{};
std::wstring replyStatus;
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
bool Foreground() {
    DWORD process{};GetWindowThreadProcessId(GetForegroundWindow(),&process);
    return process==GetCurrentProcessId();
}
std::string JsonString(const std::wstring& value) {
    const int size=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,value.data(),int(value.size()),nullptr,0,nullptr,nullptr);
    if(size<=0 && !value.empty())throw std::runtime_error("reply contains invalid text");
    std::string utf8(size,'\0'),result="\"";
    if(size)WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,value.data(),int(value.size()),utf8.data(),size,nullptr,nullptr);
    for(unsigned char c:utf8) {
        if(c=='"' || c=='\\')result+='\\';
        if(c<32) {char escape[7]{};sprintf_s(escape,"\\u%04x",c);result+=escape;}
        else result+=char(c);
    }
    return result+'"';
}
uint64_t QueueMessage(const std::wstring& heading,const std::wstring& text,unsigned seconds) {
    title=heading.substr(0,56);body=text.substr(0,180);++revision;++paintVersion;presentedAt=visibleSince=0;
    lifetime=std::clamp(seconds,1u,300u);expiresAt=GetTickCount64()+300000;failed=false;
    reply.Cancel();replyStatus.clear();acceptInput=false;answered=false;
    question=false;questionId="message-"+std::to_string(revision);
    Status(title.empty()?"cleared":"queued");return revision;
}
class MessageImage {
    ComPtr<ID3D11Texture2D> texture_;
    EyeBlit blit_;
    ULONG_PTR gdiplus_{};
    uint64_t painted_{};
public:
    void Draw(ID3D11RenderTargetView* target,unsigned w,unsigned h) {
        std::lock_guard lock(messageMutex);
        ComPtr<ID3D11Device> device;target->GetDevice(&device);
        if(!texture_) {
            Gdiplus::GdiplusStartupInput startup;
            if(!gdiplus_ && Gdiplus::GdiplusStartup(&gdiplus_,&startup,nullptr)!=Gdiplus::Ok)throw std::runtime_error("test message text initialization");
            if(!blit_.Initialize(device.Get()))throw std::runtime_error("test message blit initialization");
            D3D11_TEXTURE2D_DESC d{};d.Width=768;d.Height=1024;d.MipLevels=d.ArraySize=1;
            d.Format=DXGI_FORMAT_B8G8R8A8_UNORM;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            if(FAILED(device->CreateTexture2D(&d,nullptr,&texture_)))throw std::runtime_error("test message texture");
        }
        if(painted_!=paintVersion) {
            Gdiplus::Bitmap bitmap(768,1024,PixelFormat32bppPARGB);
            {
                Gdiplus::Graphics g(&bitmap);g.Clear(Gdiplus::Color(0,0,0,0));
                g.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAliasGridFit);
                Gdiplus::SolidBrush background(Gdiplus::Color(246,23,27,29)),white(Gdiplus::Color(255,246,246,246)),lime(Gdiplus::Color(255,185,235,45));
                g.FillRectangle(&background,12,310,744,reply.editing?670:455);g.FillRectangle(&lime,12,310,6,reply.editing?670:455);
                Gdiplus::Font label(L"Segoe UI",23,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),heading(L"Segoe UI",38,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),text(L"Segoe UI",33,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
                g.DrawString(L"DiRT2VR  /  TEST MESSAGE",-1,&label,Gdiplus::RectF(42,332,680,38),nullptr,&lime);
                g.DrawString(title.c_str(),-1,&heading,Gdiplus::RectF(42,376,680,100),nullptr,&white);
                g.DrawString(body.c_str(),-1,&text,Gdiplus::RectF(42,486,680,205),nullptr,&white);
                const auto hint=!inputReady?L"Reply input unavailable":reply.editing?L"Enter: send   Esc: cancel   Backspace: erase":answered?L"Reply saved. Enter: reply again":L"Enter: type a reply";
                g.DrawString(hint,-1,&label,Gdiplus::RectF(42,706,680,40),nullptr,&lime);
                if(reply.editing) {
                    const auto visible=reply.text.size()>150?L"..."+reply.text.substr(reply.text.size()-150):reply.text;
                    g.DrawString((visible+L"|").c_str(),-1,&text,Gdiplus::RectF(42,752,680,175),nullptr,&white);
                    const auto status=replyStatus.empty()?std::to_wstring(reply.text.size())+L" / 240":replyStatus;
                    g.DrawString(status.c_str(),-1,&label,Gdiplus::RectF(42,938,680,32),nullptr,&lime);
                }
            }
            Gdiplus::BitmapData data{};Gdiplus::Rect rect(0,0,768,1024);
            if(bitmap.LockBits(&rect,Gdiplus::ImageLockModeRead,PixelFormat32bppPARGB,&data)!=Gdiplus::Ok)throw std::runtime_error("test message bitmap");
            ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
            context->UpdateSubresource(texture_.Get(),0,nullptr,data.Scan0,UINT(data.Stride),0);bitmap.UnlockBits(&data);
            painted_=paintVersion;
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
    std::lock_guard lock(messageMutex);
    if(reply.editing)return 0; // Never replace an answer being typed.
    return QueueMessage(heading,text,seconds);
}
bool TestMessageCurrent(uint64_t value){std::lock_guard lock(messageMutex);return value && value==revision && !title.empty();}
bool TestMessageVisibleFor(uint64_t value,uint64_t milliseconds){std::lock_guard lock(messageMutex);return value && value==revision && !title.empty() && visibleSince && GetTickCount64()-visibleSince>=milliseconds;}
bool EnableTestMessageInput() {if(TestMessagesEnabled())inputReady=EnableSeatInput() && EnableMessageQueueInput();return inputReady;}
bool TestMessageTyping(){if(!TestMessagesEnabled())return false;std::lock_guard lock(messageMutex);return reply.editing;}
bool TestMessageBlocksKey(unsigned key) {
    if(!TestMessagesEnabled() || key>=256)return false;
    std::lock_guard lock(messageMutex);
    const bool down=(GetAsyncKeyState(int(key))&0x8000)!=0;
    const bool owned=inputReady && acceptInput && Foreground() && (reply.editing || key==VK_RETURN);
    const bool block=owned || keysBlocked[key];
    keysBlocked[key]=down && block;
    return block;
}
bool TestMessageWindowKey(UINT message,WPARAM key,LPARAM flags) {
    if(!TestMessagesEnabled())return false;
    std::lock_guard lock(messageMutex);
    if(message==WM_KILLFOCUS) {reply.Cancel();acceptInput=false;++paintVersion;return false;}
    if(!inputReady)return false;
    if(message==WM_KEYUP && key<256) {const bool block=keysBlocked[key];keysBlocked[key]=false;return block || (acceptInput && Foreground() && (reply.editing || key==VK_RETURN));}
    if(!acceptInput || !Foreground())return false;
    if(message==WM_CHAR && key<256 && keysBlocked[key] && (key==13 || key==27))return true;
    if(message==WM_CHAR && reply.editing) {reply.Character(wchar_t(key));++paintVersion;return true;}
    if(message!=WM_KEYDOWN || key>=256)return false;
    if(key==VK_RETURN) {
        keysBlocked[key]=true;
        if(!(flags&(1L<<30))) {
            if(!reply.editing){reply.Begin();answered=false;replyStatus.clear();}else reply.Submit();
            ++paintVersion;
        }
        return true;
    }
    if(reply.editing) {
        keysBlocked[key]=true;
        if(key==VK_ESCAPE) {reply.Cancel();++paintVersion;}
        return true;
    }
    return false;
}
void PollTestMessage() {
    if(!TestMessagesEnabled())return;
    std::lock_guard lock(messageMutex);
    if(reply.editing && !Foreground()) {reply.Cancel();acceptInput=false;++paintVersion;}
    if(reply.pending) {
        try {
            const auto text=JsonString(reply.text);
            auto out=TraceFile(Output()/"headset-replies.jsonl",std::ios::app);
            out<<"{\"version\":1,\"question_id\":\""<<questionId<<"\",\"revision\":"<<revision<<",\"tick_ms\":"<<GetTickCount64()<<",\"text\":"<<text<<"}\n";
            out.flush();if(!out)throw std::runtime_error("reply could not be saved");
            reply.Cancel();answered=true;++paintVersion;expiresAt=GetTickCount64()+120000;Status("reply-saved");
        } catch(...) {reply.pending=false;replyStatus=L"Not saved. Press Enter to retry.";++paintVersion;Status("reply-save-failed");}
    }
    if(reply.editing)return;
    auto now=GetTickCount64();if(now-lastPoll<250)return;lastPoll=now;
    auto file=Output()/"headset-question.txt";
    std::error_code existsError;const bool isQuestion=std::filesystem::exists(file,existsError);
    if(!isQuestion)file=Output()/"headset-message.txt";
    std::ifstream input(file,std::ios::binary);if(!input)return;
    std::array<char,2049> bytes{};input.read(bytes.data(),bytes.size());const auto size=input.gcount();input.close();
    std::error_code error;std::filesystem::remove(file,error);if(error)return;
    if(size>2048){Status("rejected-too-long");return;}
    if(!size){QueueMessage(L"",L"",120);return;}
    auto text=Decode(std::string(bytes.data(),size));std::string id;
    if(isQuestion) {
        const auto end=text.find(L'\n');
        if(end==std::wstring::npos || !end || end>64){Status("rejected-question-id");return;}
        for(auto c:text.substr(0,end)) {if(!((c>=L'a'&&c<=L'z')||(c>=L'A'&&c<=L'Z')||(c>=L'0'&&c<=L'9')||c==L'-'||c==L'_')){Status("rejected-question-id");return;}id+=char(c);}
        text.erase(0,end+1);
    }
    const auto split=text.find(L'\n');
    if(split==std::wstring::npos || !split || split>56 || text.size()-split-1>180){Status("rejected-format");return;}
    QueueMessage(text.substr(0,split),text.substr(split+1),120);
    if(isQuestion){question=true;questionId=id;}
}
XrFrames::Overlay* TestMessageOverlay(){return TestMessagesEnabled()?&notice:nullptr;}
void SetTestMessageReference(const XrPosef& reference) { messageReference=reference; }
void PrepareTestMessage(bool seatPanelActive) {
    std::lock_guard lock(messageMutex);
    drawn=false;notice.enabled=false;
    if(!TestMessagesEnabled() || title.empty() || failed || seatPanelActive){acceptInput=false;return;}
    if(!reply.editing && (!question || answered) && GetTickCount64()>=expiresAt){title.clear();acceptInput=false;Status("expired");return;}
    // Anchor below the recentered forward view, like the car-relative HUD.
    // Head turns/leans and message revisions must not pull the card over the road.
    notice.pose=ScreenPose(messageReference,1.6f);notice.pose.position.y-=.65f;notice.size={.9f,1.2f};
    notice.draw=[](unsigned,const XrView&,ID3D11RenderTargetView* target,uint32_t w,uint32_t h){image.Draw(target,w,h);};
    notice.unavailable=[] {std::lock_guard lock(messageMutex);failed=true;acceptInput=false;reply.Cancel();Status("unavailable");};notice.enabled=true;
}
void TestMessageSubmitted(bool submitted) {
    std::lock_guard lock(messageMutex);
    acceptInput=submitted && drawn && notice.enabled && Foreground();
    if(!submitted || !drawn){visibleSince=0;return;}
    if(!visibleSince)visibleSince=GetTickCount64();
    if(!presentedAt){presentedAt=GetTickCount64();expiresAt=presentedAt+lifetime*1000ull;Status("submitted");}
}
}
