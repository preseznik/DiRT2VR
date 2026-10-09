#include "car_setup.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <cstring>
#include <intrin.h>

namespace vr {
namespace {
using ReadFn=float (__thiscall*)(void*,unsigned);
ReadFn originalRead{};
unsigned selections[6]{};
unsigned char* host{};
// Only these six reads in the local-player vehicle creation branch. The AI
// branch skips them; menu previews and saved career setups remain untouched.
constexpr unsigned callers[]={0x2da7fc,0x2da821,0x2da846,0x2da86b,0x2da890,0x2da8b5};
float __fastcall ReadSetup(void* self,void*,unsigned index) {
    if(index>=6 || _ReturnAddress()!=host+callers[index]) return originalRead(self,index);
    // Let the native reader perform its own conversion (some axes are reversed).
    // Only its packed values and scale are read. Never modify the saved manager.
    alignas(16) unsigned char temporary[0xf2c]{};
    memcpy(temporary+0x20,static_cast<unsigned char*>(self)+0x20,4);
    unsigned packed{};
    for(unsigned i=0;i<6;++i) packed|=selections[i]<<(i*3);
    memcpy(temporary+0xf28,&packed,4);
    const float value=originalRead(temporary,index);
    Log("direct car setup: index=%u step=%u native=%.3f (player only)",index,selections[index],value);
    return value;
}
}
bool EnableCarSetup() {
    wchar_t text[16]{};
    auto length=GetEnvironmentVariableW(L"DIRT2VR_CAR_SETUP",text,16);
    if(!length) return true;
    if(length!=6 || !SupportedHost()) return false;
    for(unsigned i=0;i<6;++i) {
        if(text[i]<L'0' || text[i]>L'4') return false;
        selections[i]=text[i]-L'0';
    }
    host=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char guard[]={0x51,0x8b,0x44,0x24,0x08,0x83,0xf8,0x05};
    if(memcmp(host+0x332410,guard,sizeof(guard))) return false;
    for(auto caller:callers) {
        int displacement{}; memcpy(&displacement,host+caller-4,4);
        if(host[caller-5]!=0xe8 || static_cast<unsigned>(caller+displacement)!=0x332410) return false;
    }
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED) return false;
    status=MH_CreateHook(host+0x332410,reinterpret_cast<void*>(ReadSetup),reinterpret_cast<void**>(&originalRead));
    if(status==MH_OK) status=EnableRecordedHook(host+0x332410);
    Log("direct car setup: hook=%s steps=%ls (memory only)",MH_StatusToString(status),text);
    return status==MH_OK;
}
}
