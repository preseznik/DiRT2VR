#include "transmission.h"
#include "manual_gear_selection.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <cmath>
#include <cstring>
#include <cwchar>

namespace vr {
namespace {
using UpdateFn=void (__thiscall*)(void*,void*);
using ConstructFn=void* (__thiscall*)(void*,void*,void*);
using ActionFn=void* (__thiscall*)(void*,unsigned);
UpdateFn originalUpdate{};
ConstructFn originalConstruct{};
bool ferrari{},combined{};
// Launcher Transmission choice, 0 when the saved game setting is kept. The pairs
// match what the native profile mapping at RVA 0x2eda46 builds for each in-game
// choice: automatic 4/1, sequential 2/1, H-pattern 2/2, H-pattern + clutch 1/2.
unsigned forcedMode{},forcedGate{};
void* lastInput{};
ManualGearSelection selection;
unsigned updates{},shiftEvents{},gateEvents{};
int lastRequested{};

template<class T> T Read(const void* object,unsigned offset) {
    T value{};memcpy(&value,static_cast<const unsigned char*>(object)+offset,sizeof(value));return value;
}
template<class T> void Write(void* object,unsigned offset,T value) {
    memcpy(static_cast<unsigned char*>(object)+offset,&value,sizeof(value));
}
bool Flag(const wchar_t* name) {
    wchar_t text[8]{};
    return GetEnvironmentVariableW(name,text,8)==1 && text[0]==L'1';
}
void ReadForcedTransmission() {
    wchar_t text[20]{};
    const auto length=GetEnvironmentVariableW(L"DIRT2VR_TRANSMISSION",text,20);
    forcedMode=forcedGate=0;
    if(length==0 || length>=20)return;
    if(!wcscmp(text,L"automatic")) {forcedMode=4;forcedGate=1;}
    else if(!wcscmp(text,L"sequential")) {forcedMode=2;forcedGate=1;}
    else if(!wcscmp(text,L"hpattern")) {forcedMode=2;forcedGate=2;}
    else if(!wcscmp(text,L"hpattern_clutch")) {forcedMode=1;forcedGate=2;}
    else Log("transmission: ignoring unknown launcher choice");
}
int Gate(void* self) {
    const auto input=Read<void*>(self,0x10);
    const auto actions=Read<void*>(input,4);
    const auto get=reinterpret_cast<ActionFn>((*static_cast<void***>(actions))[3]);
    int gear=9;
    for(unsigned index=18;index<=24;++index) {
        const auto action=get(actions,index);
        if(action) {
            const auto amount=Read<float>(action,8);
            if(std::isfinite(amount) && amount>0)gear=index==24?-1:static_cast<int>(index-17);
        }
    }
    return gear;
}
void __fastcall Update(void* self,void*,void* frame) {
    const auto mode=Read<unsigned>(self,4),gateMode=Read<unsigned>(self,0x44);
    const bool active=Read<unsigned char>(self,0x40)!=0;
    const auto input=Read<void*>(self,0x10);
    const bool ready=active && input && Read<void*>(input,4);
    const auto sessionMode=ferrari?2u:forcedMode?forcedMode:mode;
    const bool manual=sessionMode==1 || sessionMode==2;
    const bool gates=combined && manual;
    if(!ready || (!ferrari && !forcedMode && !gates)) {
        selection.Reset();lastInput=nullptr;
        originalUpdate(self,frame);return;
    }
    if(lastInput!=self)selection.Reset();
    lastInput=self;
    const auto gear=gates?Gate(self):9;
    // Only the live human-input object is borrowed. The native update writes its
    // ordinary output command, then its configuration is restored before return.
    // No career setting, saved binding, AI input or physics parameter is changed.
    // The game's Manual Sequential UI selects type 3 -> command 2, with
    // automatic clutch. Command 1 is the H-pattern path with a clutch pedal.
    Write(self,4,sessionMode);
    // Gate 1 reads both Gear Up/Down and native paddle actions; gate 2 reads
    // paddles and H gears. Combined shifts read the H gears themselves.
    const auto sessionGate=ferrari || gates || !forcedMode?1u:forcedGate;
    Write(self,0x44,sessionGate);
    __try {
        originalUpdate(self,frame);
    } __finally {
        Write(self,4,mode);Write(self,0x44,gateMode);
    }
    const bool up=Read<unsigned char>(self,0x1c)!=0,down=Read<unsigned char>(self,0x1d)!=0;
    const int requested=gates?selection.Select(gear,up || down):0;
    // With gate 2 the native update has already written its H gear request here.
    if(sessionGate==1)Write(self,0x20,requested);
    if(requested!=0) {
        Write(self,0x1c,static_cast<unsigned char>(0));
        Write(self,0x1d,static_cast<unsigned char>(0));
    }
    ++updates;
    if((up || down) && shiftEvents<32) {
        ++shiftEvents;
        Log("transmission: shift up=%d down=%d mode=%u gate=%d saved_mode=%u saved_gate=%u",up,down,
            Read<unsigned>(self,0x38),requested,mode,gateMode);
    }
    if(gates && requested!=lastRequested && gateEvents<32) {
        ++gateEvents;
        Log("transmission: H request=%d held_gate=%d mode=%u saved_mode=%u saved_gate=%u",requested,gear,
            Read<unsigned>(self,0x38),mode,gateMode);
    }
    lastRequested=requested;
    if(updates==1 || updates%1200==0)
        Log("transmission: human updates=%u f04=%d combined=%d forced_mode=%u output_mode=%u profile_untouched=1 config_restored=%d",
            updates,ferrari,combined,forcedMode,Read<unsigned>(self,0x38),Read<unsigned>(self,4)==mode && Read<unsigned>(self,0x44)==gateMode);
}
void* __fastcall Construct(void* self,void*,void* source,void* allocator) {
    auto result=originalConstruct(self,source,allocator);
    selection.Reset();lastInput=nullptr;lastRequested=0;
    return result;
}
}
bool EnableTransmission() {
    wchar_t car[8]{};
    ferrari=GetEnvironmentVariableW(L"DIRT2VR_CUSTOM_CAR",car,8)==3 && !wcscmp(car,L"f04") && Flag(L"DIRT2VR_DIRECT_PRACTICE");
    combined=Flag(L"DIRT2VR_COMBINED_MANUAL_GEARS");
    ReadForcedTransmission();
    if(!ferrari && !combined && !forcedMode)return true;
    if(originalUpdate)return true;
    if(!SupportedHost() || !EnableGfwlCompatibility())return false;
    const auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    constexpr unsigned char guard[]={0x83,0xec,0x18,0x56,0x8b,0xf1,0x80,0x7e,0x40,0x00};
    constexpr unsigned char constructGuard[]={0x8b,0x54,0x24,0x08,0x0f,0x57,0xc0,0xf3,0x0f,0x10,0x0d};
    if(memcmp(base+0x729790,guard,sizeof(guard)))return false;
    if(memcmp(base+0x6f4260,constructGuard,sizeof(constructGuard)))return false;
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return false;
    status=MH_CreateHook(base+0x729790,reinterpret_cast<void*>(Update),reinterpret_cast<void**>(&originalUpdate));
    if(status==MH_OK)status=EnableRecordedHook(base+0x729790);
    if(status==MH_OK)status=MH_CreateHook(base+0x6f4260,reinterpret_cast<void*>(Construct),reinterpret_cast<void**>(&originalConstruct));
    if(status==MH_OK)status=EnableRecordedHook(base+0x6f4260);
    Log("transmission: human input hook=%s f04_sequential=%d combined_manual=%d forced_mode=%u forced_gate=%u (session only)",
        MH_StatusToString(status),ferrari,combined,forcedMode,forcedGate);
    return status==MH_OK;
}
}
