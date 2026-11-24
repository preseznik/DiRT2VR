#include "f04_audio.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <cstdlib>
#include <cstring>
#include <cwchar>

namespace vr {
namespace {
void* originalChoice{};
void* legacyChoice{};
bool choiceReady{};
// The player car whose engine is a legacy ramp bank: the converted F2004, or an
// external car pack's own engine sound (the launcher sets DIRT2VR_PACK_ENGINE).
// Its inline audio name "<code>\0" (the AI twin "<code>_ai" never matches) and
// the ramp length registered for it.
DWORD legacyCar{};
DWORD legacyLength{};

// The game allocates an empty BDL renderer even for our legacy sample bank.
// Its presence suppresses native sample lengths and RPM-based seeking, although
// the samples still play. Select the existing legacy construction path instead.
// This branch runs before allocation: no live pointer or physics value changes.
__declspec(naked) void ChooseAudio() {
    __asm {
        jz legacy
        pushfd
        push eax
        push ecx
        mov eax, [esi+38h]
        test eax, eax
        jz unchanged
        mov ecx, legacyCar
        cmp dword ptr [eax+0ch], ecx      // inline "<code>\0", excluding <code>_ai
        jne unchanged
        cmp byte ptr [eax+180h], 0        // no BDL bundle
        jne unchanged
        cmp dword ptr [eax+14h], -1       // valid legacy sample bank
        je unchanged
        mov ecx, legacyLength
        cmp dword ptr [eax+1c0h], ecx
        jne unchanged
        cmp dword ptr [eax+1c4h], ecx
        jne unchanged
        pop ecx
        pop eax
        popfd
    legacy:
        jmp dword ptr [legacyChoice]
    unchanged:
        pop ecx
        pop eax
        popfd
        jmp dword ptr [originalChoice]
    }
}

bool CarCode(const wchar_t* code) {
    for(int i=0;i<3;i++) if(!((code[i]>=L'a' && code[i]<=L'z') || (code[i]>=L'0' && code[i]<=L'9')))return false;
    return true;
}
}

bool EnableLegacyEngineAudio() {
    wchar_t car[8]{},samples[16]{},direct[8]{};
    if(GetEnvironmentVariableW(L"DIRT2VR_DIRECT_PRACTICE",direct,8)!=1 || direct[0]!=L'1')return true;
    DWORD length=0;
    if(GetEnvironmentVariableW(L"DIRT2VR_CUSTOM_CAR",car,8)==3 && !wcscmp(car,L"f04"))length=320000;
    else if(GetEnvironmentVariableW(L"DIRT2VR_PACK_ENGINE",car,8)==3 && CarCode(car)) {
        const DWORD digits=GetEnvironmentVariableW(L"DIRT2VR_PACK_ENGINE_SAMPLES",samples,16);
        if(digits>0 && digits<8)length=wcstoul(samples,nullptr,10);
    }
    // The launcher accepts pack ramps of 32000 to 1048576 frames.
    if(length<32000 || length>1048576)return true;
    legacyCar=static_cast<DWORD>(car[0]) | static_cast<DWORD>(car[1])<<8 | static_cast<DWORD>(car[2])<<16;
    legacyLength=length;
    if(originalChoice)return choiceReady;
    if(!SupportedHost() || !EnableGfwlCompatibility())return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    constexpr unsigned char guard[]={0x0f,0x84,0xca,0x03,0x00,0x00,0x8b,0x4d,0x18,0x8b,0x46,0x34};
    constexpr unsigned char legacyGuard[]={0x89,0x5e,0x24};
    if(memcmp(base+0x615618,guard,sizeof(guard)) || memcmp(base+0x6159e8,legacyGuard,sizeof(legacyGuard)))return false;
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return false;
    legacyChoice=base+0x6159e8;
    status=MH_CreateHook(base+0x615618,reinterpret_cast<void*>(ChooseAudio),&originalChoice);
    if(status==MH_OK)status=EnableRecordedHook(base+0x615618);
    Log("engine audio: native legacy RPM controller hook=%s car=%ls samples=%lu; other car identities unchanged",MH_StatusToString(status),car,length);
    choiceReady=status==MH_OK;
    return choiceReady;
}
}
