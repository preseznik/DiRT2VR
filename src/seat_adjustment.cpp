#include "seat_adjustment.h"
#include "common.h"
#include "gfwl_compat.h"
#include "eye_blit.h"
#include "vr_hotkeys.h"
#include <MinHook.h>
#include <dinput.h>
#include <gdiplus.h>
#include <atomic>
#include <map>
#include <mutex>
#include <vector>
#include <memory>
#include <cstring>

namespace vr {
namespace {
constexpr unsigned bytes=16384, table=256, stride=80, maxCars=128;
unsigned char* channel{};
std::mutex mutex;
std::map<void*,std::string> identities;
std::atomic<int> selected{-1};
int active=-1;
seat::Position current{},opening{},saved{};
seat::Movement movement;
bool panel{},dirty{},sideways{},inputReady{},eligible{};
bool keysBlocked[256]{},keysArmed[256]{};
unsigned previousHeld{};
ULONGLONG lastTick{},lastChange{};
XrPosef panelPose{{0,0,0,1},{0,0,-1}};
XrFrames::Overlay overlay;
struct Input { unsigned held{},source{},slot{}; GUID guid{}; unsigned buttons[4]{},pov{}; } input;
using SetCamerasFn=void (__thiscall*)(void*,void*);
SetCamerasFn setCameras{};
template<class T> T Read(unsigned offset) { T value{}; memcpy(&value,channel+offset,sizeof(value)); return value; }
template<class T> void Write(unsigned offset,T value) { memcpy(channel+offset,&value,sizeof(value)); }
std::string CarString(int index,unsigned offset,unsigned length) {
    if(index<0 || index>=int(Read<unsigned>(184))) return {};
    const char* value=reinterpret_cast<const char*>(channel+table+stride*index+offset);
    return std::string(value,strnlen_s(value,length));
}
void Publish(bool commit=false) {
    if(!channel) return;
    InterlockedIncrement(reinterpret_cast<LONG*>(channel+92));
    overlay.enabled=panel;
    Write(80,unsigned(panel)); Write(84,unsigned(!inputReady ? 2 : active<0 ? 1 : 0));
    Write(88,active); Write(96,current);
    if(commit && active>=0) {
        Write(108,current); Write(120,active);
        Write(124,Read<unsigned>(124)+1);
        Write(table+stride*active+64,current);
    }
    MemoryBarrier(); InterlockedIncrement(reinterpret_cast<LONG*>(channel+92));
}
void Commit() { if(active>=0 && current!=saved) { saved=current; Publish(true); } dirty=false; }
void Cancel() { if(panel) current=opening; panel=false; movement.Reset(); }
bool Key(unsigned vk) { return vk && (GetAsyncKeyState(int(vk))&0x8000)!=0; }
unsigned Modifiers() { return (Key(VK_CONTROL)?1:0)|(Key(VK_MENU)?2:0)|(Key(VK_SHIFT)?4:0); }
unsigned HeldKeys() {
    unsigned held=0;
    for(unsigned k=0;k<7;++k) {
        const auto vk=Read<unsigned>(128+k*8), mods=Read<unsigned>(132+k*8);
        if(vk>255 || mods>7 || !vk) continue;
        if(!Key(vk)) { keysArmed[vk]=true; continue; }
        if(keysArmed[vk] && Modifiers()==mods) { held|=1u<<(k==0?13:k-1); keysBlocked[vk]=true; }
    }
    if(panel) {
        constexpr unsigned nav[]={VK_SHIFT,VK_UP,VK_DOWN,VK_LEFT,VK_RIGHT,VK_RETURN,VK_ESCAPE};
        for(unsigned k=0;k<7;++k) {
            const auto vk=nav[k];
            if(!Key(vk)) keysArmed[vk]=true;
            if(keysArmed[vk] && Key(vk)) { held|=1u<<(k+6); keysBlocked[vk]=true; }
        }
    }
    return held;
}
void ReadInput() {
    auto seq=Read<unsigned>(16); if(seq&1) return;
    Input next{};
    next.held=Read<unsigned>(20); next.source=Read<unsigned>(36); next.slot=Read<unsigned>(40);
    next.guid=Read<GUID>(44); memcpy(next.buttons,channel+60,16); next.pov=Read<unsigned>(76);
    MemoryBarrier(); if(Read<unsigned>(16)!=seq) return;
    input=next;
}
void __fastcall SetCameras(void* car,void*,void* camera) {
    setCameras(car,camera);
    char code[16]{}; void* manager{}; SIZE_T read{};
    // The assignment method covers every camera creation path. The car code
    // is the field used by the cameras.xml path formatter; never a launcher guess.
    if(!ReadProcessMemory(GetCurrentProcess(),static_cast<char*>(car)+0x55c1,code,sizeof(code),&read) ||
       !ReadProcessMemory(GetCurrentProcess(),static_cast<char*>(car)+0x8384,&manager,sizeof(manager),&read) || !manager) return;
    code[15]=0;
    std::lock_guard lock(mutex);
    identities[manager]=code;
    Log("seat adjustment: camera assigned manager=%p car=%s",manager,code);
}
class PanelImage {
    Microsoft::WRL::ComPtr<ID3D11Texture2D> texture_;
    EyeBlit blit_;
    ULONG_PTR gdiplus_{};
    std::wstring last_;
    std::unique_ptr<Gdiplus::Bitmap> bitmap_;
public:
    void Draw(ID3D11RenderTargetView* target,unsigned w,unsigned h) {
        Microsoft::WRL::ComPtr<ID3D11Device> device; target->GetDevice(&device);
        if(!texture_) {
            Gdiplus::GdiplusStartupInput startup;
            if(Gdiplus::GdiplusStartup(&gdiplus_,&startup,nullptr)!=Gdiplus::Ok || !blit_.Initialize(device.Get())) throw std::runtime_error("seat panel initialization");
            D3D11_TEXTURE2D_DESC d{}; d.Width=768; d.Height=1024; d.MipLevels=1; d.ArraySize=1;
            d.Format=DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleDesc.Count=1; d.Usage=D3D11_USAGE_DEFAULT; d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            if(FAILED(device->CreateTexture2D(&d,nullptr,&texture_))) throw std::runtime_error("seat panel texture");
        }
        std::wstring name;
        const auto text=CarString(active,16,48);
        int n=MultiByteToWideChar(CP_UTF8,0,text.data(),int(text.size()),nullptr,0);
        name.resize(n); MultiByteToWideChar(CP_UTF8,0,text.data(),int(text.size()),name.data(),n);
        wchar_t values[256]{};
        swprintf_s(values,L"Height     %+.1f cm\nDepth      %+.1f cm\nSideways  %+.1f cm",current.y*100,-current.z*100,current.x*100);
        wchar_t keyName[64]{};
        const auto vk=Read<unsigned>(128),mods=Read<unsigned>(132);
        GetKeyNameTextW(LONG(MapVirtualKeyW(vk,MAPVK_VK_TO_VSC)<<16),keyName,64);
        std::wstring close=std::wstring(mods&1?L"Ctrl+":L"")+(mods&2?L"Alt+":L"")+(mods&4?L"Shift+":L"")+keyName+L": save & close";
        std::wstring hint(reinterpret_cast<const wchar_t*>(channel+10500),wcsnlen_s(reinterpret_cast<const wchar_t*>(channel+10500),400));
        const auto signature=name+values+close+hint+(sideways?L"side":L"depth")+(inputReady?L"":L"disabled");
        if(signature!=last_) {
            if(!bitmap_) bitmap_=std::make_unique<Gdiplus::Bitmap>(768,1024,PixelFormat32bppPARGB);
            auto& bitmap=*bitmap_;
            Gdiplus::Graphics g(&bitmap); g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
            g.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAliasGridFit);
            g.Clear(Gdiplus::Color(238,25,27,29));
            Gdiplus::SolidBrush white(Gdiplus::Color(255,245,245,245)),green(Gdiplus::Color(255,183,235,25));
            Gdiplus::Font title(L"Segoe UI",43,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),normal(L"Segoe UI",29,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel),bold(L"Segoe UI",32,Gdiplus::FontStyleBold,Gdiplus::UnitPixel);
            auto label=[&](const wchar_t* s,float x,float y,float width,float height,Gdiplus::Font& f,Gdiplus::Brush& brush) { g.DrawString(s,-1,&f,Gdiplus::RectF(x,y,width,height),nullptr,&brush); };
            label(L"SEAT POSITION",40,30,690,65,title,white);
            label(active<0?L"Car not identified":name.c_str(),40,98,690,80,normal,white);
            label(close.c_str(),40,174,690,45,normal,green);
            // Seat pictogram, deliberately vector drawn rather than a font glyph.
            Gdiplus::Pen seatPen(Gdiplus::Color(255,245,245,245),24);
            const Gdiplus::PointF seatPoints[]={{330,315},{347,426},{364,480},{445,480},{480,520}};
            g.DrawLines(&seatPen,seatPoints,5);
            auto arrow=[&](float x,float y,int dx,int dy) {
                Gdiplus::Pen pen(Gdiplus::Color(255,183,235,25),9); g.DrawLine(&pen,x-dx*23,y-dy*23,x+dx*23,y+dy*23);
                Gdiplus::PointF triangle[]={{x+dx*40,y+dy*40},{x+dx*12-dy*20,y+dy*12+dx*20},{x+dx*12+dy*20,y+dy*12-dx*20}};
                g.FillPolygon(&green,triangle,3);
            };
            arrow(384,254,0,-1); arrow(384,555,0,1); arrow(126,406,-1,0); arrow(642,406,1,0);
            label(L"UP",356,286,90,45,bold,white); label(L"DOWN",335,597,130,45,bold,white);
            label(sideways?L"LEFT":L"BACK",55,451,180,45,bold,white); label(sideways?L"RIGHT":L"FORWARD",550,451,205,45,bold,white);
            label(values,175,670,550,145,bold,white);
            label(!inputReady?L"Input filtering unavailable":active<0?L"Adjustment unavailable for this car":L"Arrows / D-pad: move",40,838,690,45,normal,white);
            label(hint.c_str(),40,886,690,120,normal,white);
            Gdiplus::BitmapData data{}; Gdiplus::Rect rect(0,0,768,1024);
            if(bitmap.LockBits(&rect,Gdiplus::ImageLockModeRead,PixelFormat32bppPARGB,&data)!=Gdiplus::Ok) throw std::runtime_error("seat panel bitmap");
            Microsoft::WRL::ComPtr<ID3D11DeviceContext> dc; device->GetImmediateContext(&dc);
            dc->UpdateSubresource(texture_.Get(),0,nullptr,data.Scan0,UINT(data.Stride),0); bitmap.UnlockBits(&data);
            last_=signature;
        }
        if(!blit_.Draw(0,texture_.Get(),target,w,h,true)) throw std::runtime_error("seat panel copy");
    }
};
PanelImage panelImage;
}
bool EnableSeatAdjustment() {
    wchar_t name[128]{};
    if(!GetEnvironmentVariableW(L"DIRT2VR_SEAT_CHANNEL",name,128)) return true;
    HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name); if(!mapping) return false;
    channel=static_cast<unsigned char*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,bytes)); CloseHandle(mapping);
    if(!channel) return false;
    if(Read<unsigned>(0)!=0x54414553 || Read<unsigned>(4)!=1 || Read<unsigned>(8)!=bytes || Read<unsigned>(184)>maxCars || Read<unsigned>(11300)>64) { UnmapViewOfFile(channel); channel=nullptr; return false; }
    if(!SupportedHost()) { UnmapViewOfFile(channel); channel=nullptr; return false; }
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char guard[]={0x56,0x8b,0xf1,0x8b,0x4c,0x24,0x08,0x85,0xc9,0x74,0x3e,0x8d,0x86,0xd0,0x81,0,0};
    if(memcmp(base+0x99f4c0,guard,sizeof(guard))) { UnmapViewOfFile(channel); channel=nullptr; return false; }
    auto result=MH_CreateHook(base+0x99f4c0,reinterpret_cast<void*>(SetCameras),reinterpret_cast<void**>(&setCameras));
    if(result==MH_OK) result=EnableRecordedHook(base+0x99f4c0);
    for(unsigned i=0;i<256;++i) keysArmed[i]=!Key(i);
    inputReady=result==MH_OK && EnableSeatInput();
    SetPanelKeyFilter(SeatWindowKey);
    overlay.enabled=false;
    overlay.unavailable=[] { SeatInactive(); };
    overlay.size={.6f,.8f}; overlay.draw=[](unsigned,const XrView&,ID3D11RenderTargetView* target,unsigned w,unsigned h) { panelImage.Draw(target,w,h); };
    Log("seat adjustment: camera hook=%s input=%d",MH_StatusToString(result),inputReady);
    return result==MH_OK && inputReady;
}
void SeatSelectCamera(void* manager) {
    if(!channel) return;
    std::lock_guard lock(mutex);
    int index=-1;
    if(auto found=identities.find(manager);found!=identities.end()) {
        for(unsigned i=0;i<Read<unsigned>(184);++i) if(CarString(int(i),0,16)==found->second) { index=int(i); break; }
    }
    selected=index;
    Log("seat adjustment: active car=%s manager=%p known=%u",index>=0 ? CarString(index,0,16).c_str() : "unknown",manager,unsigned(identities.size()));
}
void SeatInactive() {
    if(!channel) return;
    std::lock_guard lock(mutex);
    eligible=false; Cancel(); if(dirty) Commit(); previousHeld=~0u; lastTick=0; Publish();
    for(unsigned i=0;i<256;++i) keysArmed[i]=!Key(i);
}
void SeatPrepare(const std::array<XrView,2>& views) {
    if(!channel) return;
    std::lock_guard lock(mutex);
    eligible=inputReady;
    const auto now=GetTickCount64(); const double dt=lastTick ? double(now-lastTick)/1000 : 0; lastTick=now;
    if(selected!=active) {
        Cancel(); if(dirty) Commit(); active=selected; current=saved=active>=0 ? Read<seat::Position>(table+active*stride+64) : seat::Position{};
        if(!seat::Valid(current)) current=saved={}; previousHeld=~0u;
    }
    ReadInput();
    if(DWORD(GetTickCount()-Read<DWORD>(12))>1000) { Cancel(); input={}; }
    DWORD foreground{}; GetWindowThreadProcessId(GetForegroundWindow(),&foreground);
    if(foreground!=GetCurrentProcessId()) { Cancel(); previousHeld=~0u; Publish(); return; }
    const unsigned held=input.held|HeldKeys(),pressed=held&~previousHeld; previousHeld=held;
    if(pressed&(1u<<13)) {
        if(panel) { Commit(); panel=false; }
        else if(inputReady) { Commit(); opening=current; panel=true; panelPose=ScreenPose(CenterPose(views),1.f); movement.Reset();
            for(unsigned vk:{VK_SHIFT,VK_UP,VK_DOWN,VK_LEFT,VK_RIGHT,VK_RETURN,VK_ESCAPE}) keysArmed[vk]=!Key(vk); }
    }
    if(panel && (pressed&(1u<<12))) Cancel();
    else if(panel && (pressed&(1u<<11))) { Commit(); panel=false; }
    sideways=(held&(1u<<6))!=0;
    unsigned directions=held&63;
    if(panel) {
        if(held&(1u<<7)) directions|=4; if(held&(1u<<8)) directions|=8;
        if(held&(1u<<9)) directions|=sideways?1:16;
        if(held&(1u<<10)) directions|=sideways?2:32;
    }
    if(inputReady && active>=0 && movement.Update(current,directions,dt)) { dirty=!panel; lastChange=now; }
    if(dirty && !directions && now-lastChange>=300) Commit();
    overlay.pose=panelPose; Publish();
}
XrPosef SeatEyePose(XrPosef relative) { relative.position.x+=current.x; relative.position.y+=current.y; relative.position.z+=current.z; return relative; }
XrPosef SeatHudPose(XrPosef pose,const XrPosef& reference) {
    // Convert the car-relative offset into OpenXR LOCAL; keep HUD in the car.
    auto x=reference; x.position={0,0,0};
    const auto& q=x.orientation;
    const XrVector3f v{-current.x,-current.y,-current.z};
    const XrVector3f t{2*(q.y*v.z-q.z*v.y),2*(q.z*v.x-q.x*v.z),2*(q.x*v.y-q.y*v.x)};
    pose.position.x+=v.x+q.w*t.x+q.y*t.z-q.z*t.y;
    pose.position.y+=v.y+q.w*t.y+q.z*t.x-q.x*t.z;
    pose.position.z+=v.z+q.w*t.z+q.x*t.y-q.y*t.x;
    return pose;
}
XrFrames::Overlay* SeatOverlay() { return channel ? &overlay : nullptr; }
bool SeatInputReady() { return inputReady; }
bool SeatBlocksKey(unsigned key) {
    if(!channel || key>255) return false;
    std::lock_guard lock(mutex);
    bool block=keysBlocked[key];
    if(eligible) for(unsigned k=0;k<7;++k) if(key && key==Read<unsigned>(128+k*8) && Modifiers()==Read<unsigned>(132+k*8)) block=keysBlocked[key]=true;
    if(panel && (key==VK_SHIFT || key==VK_LSHIFT || key==VK_RSHIFT || key==VK_RETURN || key==VK_ESCAPE || (key>=VK_LEFT&&key<=VK_DOWN))) block=true;
    if(!Key(key)) keysBlocked[key]=false;
    return block;
}
bool SeatWindowKey(UINT message,WPARAM key) {
    if(message==WM_CHAR && (key==9 || key==13 || key==27)) {
        std::lock_guard lock(mutex); if(panel || keysBlocked[key]) return true;
    }
    return (message==WM_KEYDOWN || message==WM_KEYUP || message==WM_SYSKEYDOWN || message==WM_SYSKEYUP) && SeatBlocksKey(unsigned(key));
}
namespace {
struct Reserved { unsigned buttons[4]{},pov{}; };
std::map<std::string,Reserved> heldReservations;
Reserved ReservedFor(unsigned source,unsigned slot,const GUID& guid) {
    Reserved mask;
    if(!eligible) return mask;
    for(unsigned i=0;i<Read<unsigned>(11300);++i) {
        const unsigned at=11304+i*48;
        if(Read<unsigned>(at)!=source || (source==1 ? Read<unsigned>(at+4)!=slot : Read<GUID>(at+8)!=guid)) continue;
        if(Read<unsigned>(at+44)>=9 && !panel) continue;
        for(unsigned j=0;j<4;++j) mask.buttons[j]|=Read<unsigned>(at+24+j*4);
        mask.pov|=Read<unsigned>(at+40);
    }
    const bool ownsPanel=panel && (!input.source || (source==input.source && (source==1 ? slot==input.slot : guid==input.guid)));
    if(ownsPanel) {
        if(source==1) mask.buttons[0]|=0x700f; // D-pad, X, A, B.
        else mask.pov|=1;
    }
    return mask;
}
}
void SeatFilterXbox(unsigned slot,WORD& buttons) {
    if(!channel || slot>3) return; std::lock_guard lock(mutex);
    const auto reserved=ReservedFor(1,slot,{});
    auto& tail=heldReservations["x"+std::to_string(slot)];
    const WORD mask=WORD(reserved.buttons[0]|tail.buttons[0]);
    tail.buttons[0]=mask&buttons;
    buttons&=~mask;
}
void SeatFilterController(const GUID& device,bool keyboard,unsigned,unsigned type,void* value,unsigned size) {
    if(!channel) return;
    const auto instance=DIDFT_GETINSTANCE(type);
    if(keyboard && size==1) {
        const auto vk=MapVirtualKeyW(instance&0x7f,MAPVK_VSC_TO_VK_EX);
        unsigned key=vk;
        if(instance==DIK_UP) key=VK_UP; if(instance==DIK_DOWN) key=VK_DOWN;
        if(instance==DIK_LEFT) key=VK_LEFT; if(instance==DIK_RIGHT) key=VK_RIGHT;
        if(SeatBlocksKey(key)) *static_cast<BYTE*>(value)=0;
        return;
    }
    std::lock_guard lock(mutex);
    const auto reserved=ReservedFor(2,0,device);
    auto& tail=heldReservations[std::string(reinterpret_cast<const char*>(&device),sizeof(device))];
    if((type&DIDFT_BUTTON) && size==1 && instance<128) {
        const auto bit=1u<<(instance%32); auto& held=tail.buttons[instance/32];
        if((reserved.buttons[instance/32]|held)&bit) {
            if(*static_cast<BYTE*>(value)&128) held|=bit; else held&=~bit;
            *static_cast<BYTE*>(value)=0;
        }
    } else if((type&DIDFT_POV) && size==4 && instance<4) {
        const auto bit=1u<<instance;
        if((reserved.pov|tail.pov)&bit) {
            auto& v=*static_cast<DWORD*>(value);
            if(LOWORD(v)==0xffff) tail.pov&=~bit; else tail.pov|=bit;
            v=0xffffffff;
        }
    }
}
}
