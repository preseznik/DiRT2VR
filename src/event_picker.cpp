#include "event_picker.h"
#include "seat_adjustment.h"
#include "common.h"
#include "eye_blit.h"
#include <gdiplus.h>
#include <atomic>
#include <mutex>
#include <string>
#include <vector>
#include <memory>
#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace vr {
namespace {
// Written by the launcher's NextEventChannel. Tracks: flags (1 circuit),
// discipline at +16 and label at +64; cars: label. The panel writes the chosen
// mode, track, car, opponents and laps back at 20-36 and counts starts at 40.
constexpr unsigned bytes=20480,trackTable=256,trackStride=160,carTable=10496,carStride=128,maxItems=64;
enum Row { Mode,Discipline,Track,Car,Opponents,Laps,Start,Cancel,Rows };
enum class Status { Closed,Open,Started,Cancelled };
struct TrackEntry { std::wstring discipline,label; bool circuit{}; };
std::mutex mutex;
unsigned char* channel{};
bool loaded{},unavailable{};
std::vector<TrackEntry> tracks;
std::vector<std::wstring> cars;
std::atomic<Status> status{Status::Closed};
std::atomic<ULONGLONG> lastPoll{};
const void* menuState{};
int mode{},track{},car{},opponents=7,laps=1,row=Track;
unsigned repeatDirection{};
double repeatHeld{};
template<class T> T Read(unsigned offset) { T value{}; memcpy(&value,channel+offset,sizeof(value)); return value; }
template<class T> void Write(unsigned offset,T value) { memcpy(channel+offset,&value,sizeof(value)); }
std::wstring Text(unsigned offset,unsigned characters) {
    const auto* value=reinterpret_cast<const wchar_t*>(channel+offset);
    return std::wstring(value,wcsnlen_s(value,characters));
}
bool Load() {
    if(loaded || unavailable) return loaded;
    unavailable=true;
    wchar_t name[128]{};
    if(!GetEnvironmentVariableW(L"DIRT2VR_EVENT_CHANNEL",name,128)) return false;
    HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name); if(!mapping) return false;
    channel=static_cast<unsigned char*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,bytes)); CloseHandle(mapping);
    if(!channel) return false;
    const auto trackCount=Read<unsigned>(12),carCount=Read<unsigned>(16);
    if(Read<unsigned>(0)!=0x544E5645 || Read<unsigned>(4)!=1 || Read<unsigned>(8)!=bytes || !trackCount || trackCount>maxItems || !carCount || carCount>maxItems) {
        UnmapViewOfFile(channel); channel=nullptr; return false;
    }
    for(unsigned i=0;i<trackCount;++i) {
        const unsigned at=trackTable+i*trackStride;
        tracks.push_back({Text(at+16,24),Text(at+64,48),(Read<unsigned>(at)&1)!=0});
    }
    for(unsigned i=0;i<carCount;++i) cars.push_back(Text(carTable+i*carStride,48));
    loaded=true; unavailable=false;
    return true;
}
void Open(const void* state) {
    mode=std::clamp(Read<int>(20),0,1);
    track=std::clamp(Read<int>(24),0,int(tracks.size())-1);
    car=std::clamp(Read<int>(28),0,int(cars.size())-1);
    opponents=std::clamp(Read<int>(32),1,7);
    laps=std::clamp(Read<int>(36),1,20);
    row=Track; repeatDirection=0; repeatHeld=0; menuState=state;
    status=Status::Open;
}
bool Available(int r) { return (r!=Opponents || mode==1) && (r!=Laps || tracks[track].circuit); }
void Move(int d) { int r=row; do { r=(r+d+Rows)%Rows; } while(!Available(r)); row=r; }
void Change(int d) {
    const int trackCount=int(tracks.size());
    switch(row) {
    case Mode: mode^=1; break;
    case Discipline: {
        // The first track of the next discipline, in the launcher's order.
        std::vector<std::wstring> order;
        for(const auto& t:tracks) if(std::find(order.begin(),order.end(),t.discipline)==order.end()) order.push_back(t.discipline);
        const int count=int(order.size()),index=int(std::find(order.begin(),order.end(),tracks[track].discipline)-order.begin());
        const auto& next=order[(index+d+count)%count];
        track=int(std::find_if(tracks.begin(),tracks.end(),[&](const TrackEntry& t) { return t.discipline==next; })-tracks.begin());
        break;
    }
    case Track: { int t=track; do { t=(t+d+trackCount)%trackCount; } while(tracks[t].discipline!=tracks[track].discipline); track=t; break; }
    case Car: car=(car+d+int(cars.size()))%int(cars.size()); break;
    case Opponents: opponents=std::clamp(opponents+d,1,7); break;
    case Laps: laps=std::clamp(laps+d,1,20); break;
    }
}
void Finish(bool start) {
    if(start) {
        Write(20,mode); Write(24,track); Write(28,car); Write(32,opponents); Write(36,laps);
        MemoryBarrier(); Write(40,Read<unsigned>(40)+1);
    }
    status=start ? Status::Started : Status::Cancelled;
    Log("next event: %s mode=%d track=%d car=%d opponents=%d laps=%d",start?"start":"cancel",mode,track,car,opponents,laps);
}
class PickerImage {
    Microsoft::WRL::ComPtr<ID3D11Texture2D> texture_;
    EyeBlit blit_;
    ULONG_PTR gdiplus_{};
    std::wstring last_;
    std::unique_ptr<Gdiplus::Bitmap> bitmap_;
public:
    void Draw(ID3D11RenderTargetView* target,unsigned w,unsigned h,float x,float y) {
        Microsoft::WRL::ComPtr<ID3D11Device> device; target->GetDevice(&device);
        if(!texture_) {
            Gdiplus::GdiplusStartupInput startup;
            if(Gdiplus::GdiplusStartup(&gdiplus_,&startup,nullptr)!=Gdiplus::Ok || !blit_.Initialize(device.Get())) throw std::runtime_error("event panel initialization");
            D3D11_TEXTURE2D_DESC d{}; d.Width=768; d.Height=1024; d.MipLevels=1; d.ArraySize=1;
            d.Format=DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleDesc.Count=1; d.Usage=D3D11_USAGE_DEFAULT; d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            if(FAILED(device->CreateTexture2D(&d,nullptr,&texture_))) throw std::runtime_error("event panel texture");
        }
        const std::wstring values[]={mode?L"Race":L"Practice",tracks[track].discipline,tracks[track].label,cars[car],
            mode?std::to_wstring(opponents):L"Race only",tracks[track].circuit?std::to_wstring(laps):L"Stage, no laps"};
        std::wstring signature=std::to_wstring(row);
        for(const auto& v:values) signature+=L"|"+v;
        if(signature!=last_) {
            if(!bitmap_) bitmap_=std::make_unique<Gdiplus::Bitmap>(768,1024,PixelFormat32bppPARGB);
            Gdiplus::Graphics g(bitmap_.get()); g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
            g.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAliasGridFit);
            g.Clear(Gdiplus::Color(238,25,27,29));
            Gdiplus::SolidBrush white(Gdiplus::Color(255,245,245,245)),grey(Gdiplus::Color(255,150,150,150)),green(Gdiplus::Color(255,183,235,25)),
                dark(Gdiplus::Color(255,25,27,29)),highlight(Gdiplus::Color(255,52,56,60));
            Gdiplus::Font title(L"Segoe UI",43,Gdiplus::FontStyleBold,Gdiplus::UnitPixel),normal(L"Segoe UI",26,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel),
                caption(L"Segoe UI",24,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel),bold(L"Segoe UI",28,Gdiplus::FontStyleBold,Gdiplus::UnitPixel);
            Gdiplus::StringFormat line; line.SetFormatFlags(Gdiplus::StringFormatFlagsNoWrap); line.SetTrimming(Gdiplus::StringTrimmingEllipsisCharacter);
            Gdiplus::StringFormat centered; centered.SetAlignment(Gdiplus::StringAlignmentCenter); centered.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            g.DrawString(L"NEXT EVENT",-1,&title,Gdiplus::RectF(40,28,690,60),nullptr,&white);
            g.DrawString(L"Starts after a quick game restart.",-1,&normal,Gdiplus::RectF(40,90,690,40),nullptr,&grey);
            const wchar_t* labels[]={L"Event",L"Discipline",L"Track",L"Car",L"Opponents",L"Laps"};
            for(int r=Mode;r<=Laps;++r) {
                const float top=150.f+r*92;
                const bool available=Available(r),current=r==row;
                if(current) {
                    g.FillRectangle(&highlight,24.f,top,720.f,86.f); g.FillRectangle(&green,24.f,top,6.f,86.f);
                    for(int side:{-1,1}) {
                        const float cx=side<0?46.f:722.f,cy=top+57;
                        const Gdiplus::PointF arrow[]={{cx+side*9,cy},{cx-side*7,cy-12},{cx-side*7,cy+12}};
                        g.FillPolygon(&green,arrow,3);
                    }
                }
                g.DrawString(labels[r],-1,&caption,Gdiplus::RectF(64,top+6,640,30),&line,&grey);
                g.DrawString(values[r].c_str(),-1,&bold,Gdiplus::RectF(64,top+36,640,44),&line,available?&white:&grey);
            }
            for(int r:{Start,Cancel}) {
                const Gdiplus::RectF box(40,r==Start?716.f:804.f,688,78);
                if(r==row) g.FillRectangle(r==Start?&green:&white,box);
                else { Gdiplus::Pen pen(Gdiplus::Color(255,150,150,150),3); g.DrawRectangle(&pen,box); }
                g.DrawString(r==Start?L"START EVENT":L"CANCEL",-1,&bold,box,&centered,r==row?&dark:&white);
            }
            g.DrawString(L"Up/Down: choose    Left/Right: change\nEnter / A: select    Esc / B: cancel",-1,&normal,Gdiplus::RectF(40,902,690,100),nullptr,&white);
            Gdiplus::BitmapData data{}; Gdiplus::Rect rect(0,0,768,1024);
            if(bitmap_->LockBits(&rect,Gdiplus::ImageLockModeRead,PixelFormat32bppPARGB,&data)!=Gdiplus::Ok) throw std::runtime_error("event panel bitmap");
            Microsoft::WRL::ComPtr<ID3D11DeviceContext> dc; device->GetImmediateContext(&dc);
            dc->UpdateSubresource(texture_.Get(),0,nullptr,data.Scan0,UINT(data.Stride),0); bitmap_->UnlockBits(&data);
            last_=signature;
        }
        if(!blit_.Draw(0,texture_.Get(),target,w,h,true,x,y)) throw std::runtime_error("event panel copy");
    }
};
PickerImage image;
}
bool EventPickerMenu(void* state,void* result) {
    if(std::memcmp(static_cast<const char*>(state)+4,"d2vr_next_event",sizeof("d2vr_next_event"))) return false;
    const char* outcome="";
    {
        std::lock_guard lock(mutex);
        lastPoll=GetTickCount64();
        auto current=status.load();
        if(current==Status::Open && menuState!=state) { Log("next event: menu state replaced; panel reopened"); current=Status::Closed; }
        if(current==Status::Closed) {
            if(Load() && SeatInputReady()) { Open(state); Log("next event: panel opened"); }
            else { outcome="disabled"; Log("next event: panel unavailable channel=%d input=%d",loaded,SeatInputReady()); }
        } else if(current!=Status::Open) {
            outcome=current==Status::Started ? "enabled" : "disabled"; status=Status::Closed; menuState=nullptr;
        }
    }
    using Assign=void(__thiscall*)(void*,const char*);
    reinterpret_cast<Assign>(reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr))+0x6d4830)(result,outcome);
    return true;
}
bool EventPickerOpen() {
    if(status!=Status::Open) return false;
    // The menu polls its waiting row; if it stops, never keep the keys captured.
    if(GetTickCount64()-lastPoll>3000) {
        std::lock_guard lock(mutex);
        if(status==Status::Open) { status=Status::Closed; menuState=nullptr; Log("next event: menu stopped waiting; panel closed"); }
        return false;
    }
    return true;
}
void EventPickerInput(unsigned held,unsigned pressed,double dt) {
    std::lock_guard lock(mutex);
    if(status!=Status::Open) return;
    if(pressed&(1u<<12)) { Finish(false); return; }
    if(pressed&(1u<<11)) {
        if(row==Start || row==Cancel) Finish(row==Start);
        else Move(1);
        return;
    }
    // Arrows and D-pad step once, then repeat while held.
    constexpr unsigned up=1u<<7,down=1u<<8,left=1u<<9;
    const unsigned directions=held&(0xfu<<7),active=directions&(0u-directions);
    int steps=0;
    if(active!=repeatDirection) { repeatHeld=0; steps=active!=0; }
    else if(active) { const double before=repeatHeld; repeatHeld+=dt; steps=int(std::max(0.0,repeatHeld-.4)/.08)-int(std::max(0.0,before-.4)/.08); }
    repeatDirection=active;
    for(int i=0;i<steps;++i) {
        if(active==up) Move(-1); else if(active==down) Move(1);
        else if(row<Start) Change(active==left?-1:1);
    }
}
void EventPickerDraw(ID3D11RenderTargetView* target,unsigned w,unsigned h,float x,float y) {
    std::lock_guard lock(mutex);
    if(!loaded) return;
    image.Draw(target,w,h,x,y);
}
}
