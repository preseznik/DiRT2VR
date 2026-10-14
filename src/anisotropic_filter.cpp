#include "anisotropic_filter.h"
#include "common.h"
#include <MinHook.h>
#include <intrin.h>
#include <atomic>
#include <cwchar>

namespace vr {
namespace {
using CreateSampler=HRESULT(STDMETHODCALLTYPE*)(ID3D11Device*,const D3D11_SAMPLER_DESC*,ID3D11SamplerState**);
CreateSampler original{};
void* hookedTarget{};
unsigned strength{};
uintptr_t hostBegin{},hostEnd{};
std::atomic<unsigned> reported{};
HRESULT STDMETHODCALLTYPE Create(ID3D11Device* device,const D3D11_SAMPLER_DESC* desc,ID3D11SamplerState** state) {
    const auto caller=reinterpret_cast<uintptr_t>(_ReturnAddress());
    if(!desc || caller<hostBegin || caller>=hostEnd) return original(device,desc,state);
    auto adjusted=*desc;
    if(!AdjustAnisotropy(adjusted,strength)) return original(device,desc,state);
    const auto hr=original(device,&adjusted,state);
    if(reported.fetch_add(1)<16) Log("anisotropic filtering: filter=0x%x/%u -> 0x%x/%u hr=0x%08x",desc->Filter,desc->MaxAnisotropy,adjusted.Filter,adjusted.MaxAnisotropy,hr);
    // Preserve a usable game sampler if a driver rejects the optional override.
    return FAILED(hr) ? original(device,desc,state) : hr;
}
}
bool EnableAnisotropicFiltering(ID3D11Device* device) {
    wchar_t value[16]{};
    const auto length=GetEnvironmentVariableW(L"DIRT2VR_ANISOTROPY",value,16);
    if(!length) return true;
    if(length>=16) return false;
    unsigned requested{};
    for(const wchar_t* p=value;*p;++p) { if(*p<L'0'||*p>L'9'||requested>16) return false; requested=requested*10+(*p-L'0'); }
    if(!ValidAnisotropy(requested)) return false;
    if(!requested) return true;
    if(!device || !SupportedHost()) return false;
    const auto target=(*reinterpret_cast<void***>(device))[23]; // ID3D11Device::CreateSamplerState
    if(hookedTarget) return hookedTarget==target && strength==requested;
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED) return false;
    hostBegin=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const auto dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(hostBegin);
    const auto nt=reinterpret_cast<const IMAGE_NT_HEADERS*>(hostBegin+dos->e_lfanew);
    hostEnd=hostBegin+nt->OptionalHeader.SizeOfImage;
    strength=requested;
    status=MH_CreateHook(target,reinterpret_cast<void*>(Create),reinterpret_cast<void**>(&original));
    if(status==MH_OK) status=MH_EnableHook(target); // D3D runtime code, not the game's GFWL-checksummed image.
    Log("anisotropic filtering: requested=%u hook=%s",requested,MH_StatusToString(status));
    if(status!=MH_OK) return false;
    hookedTarget=target;
    return true;
}
}
