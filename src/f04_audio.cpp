#include "f04_audio.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <cstring>
#include <cwchar>

namespace vr {
namespace {
void* originalChoice{};
void* legacyChoice{};
bool choiceReady{};

// The game allocates an empty BDL renderer even for our legacy sample bank.
// Its presence suppresses native sample lengths and RPM-based seeking, although
// the samples still play. Select the existing legacy construction path instead.
// This branch runs before allocation: no live pointer or physics value changes.
__declspec(naked) void ChooseAudio() {
    __asm {
        jz legacy
        pushfd
        push eax
        mov eax, [esi+38h]
        test eax, eax
        jz unchanged
        cmp dword ptr [eax+0ch], 00343066h // inline "f04\0", excluding f04_ai
        jne unchanged
        cmp byte ptr [eax+180h], 0        // no BDL bundle
        jne unchanged
        cmp dword ptr [eax+14h], -1       // valid legacy sample bank
        je unchanged
        cmp dword ptr [eax+1c0h], 320000
        jne unchanged
        cmp dword ptr [eax+1c4h], 320000
        jne unchanged
        pop eax
        popfd
    legacy:
        jmp dword ptr [legacyChoice]
    unchanged:
        pop eax
        popfd
        jmp dword ptr [originalChoice]
    }
}
}

bool EnableF04Audio() {
    wchar_t car[8]{},direct[8]{};
    if(GetEnvironmentVariableW(L"DIRT2VR_CUSTOM_CAR",car,8)!=3 || wcscmp(car,L"f04") ||
       GetEnvironmentVariableW(L"DIRT2VR_DIRECT_PRACTICE",direct,8)!=1 || direct[0]!=L'1')return true;
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
    Log("f04 audio: native legacy RPM controller hook=%s; other car identities unchanged",MH_StatusToString(status));
    choiceReady=status==MH_OK;
    return choiceReady;
}
}
