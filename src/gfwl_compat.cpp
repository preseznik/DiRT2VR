#include "gfwl_compat.h"
#include "code_patch_view.h"
#include "common.h"
#include <bcrypt.h>
#include <array>
#include <atomic>
#include <fstream>
#include <intrin.h>
#include <mutex>

namespace vr {
namespace {
using HashFn=unsigned (__stdcall*)(const void*,unsigned);
HashFn originalHash{};
void* verifyCaller{};
bool recording{};
std::mutex patchesMutex;
std::vector<CodePatch> patches;
std::atomic<unsigned> corrected{};
bool KnownFile(HMODULE module) {
    wchar_t path[32768]{};
    if(!GetModuleFileNameW(module,path,32768)) return false;
    std::ifstream file(path,std::ios::binary);
    if(!file) return false;
    BCRYPT_ALG_HANDLE alg{}; BCRYPT_HASH_HANDLE hash{};
    if(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0) return false;
    if(BCryptCreateHash(alg,&hash,nullptr,0,nullptr,0,0)<0) { BCryptCloseAlgorithmProvider(alg,0); return false; }
    std::array<char,65536> buffer{};
    bool ok=true;
    while(file) {
        file.read(buffer.data(),buffer.size());
        if(BCryptHashData(hash,reinterpret_cast<PUCHAR>(buffer.data()),static_cast<ULONG>(file.gcount()),0)<0) ok=false;
    }
    std::array<UCHAR,32> digest{};
    ok=ok&&!file.bad()&&BCryptFinishHash(hash,digest.data(),32,0)>=0;
    BCryptDestroyHash(hash); BCryptCloseAlgorithmProvider(alg,0);
    constexpr std::array<UCHAR,32> expected={0x8a,0xe3,0x28,0xcc,0x7e,0x9f,0x22,0xa8,0xed,0x1b,0x63,0xf7,0xf0,0xc4,0x97,0x7e,
        0x36,0xbc,0x6c,0x7f,0x75,0x82,0xf1,0xb8,0xe4,0x1f,0x1f,0xdf,0x96,0x0d,0x57,0x96};
    return ok&&digest==expected;
}
unsigned HashImage(const void* data,unsigned words,void* caller) {
    // Only PEVerifyHash's in-memory comparison, not catalog/signature checks,
    // profile protection, or any other caller of the shared hash primitive.
    if(caller!=verifyCaller || !data || !words || words>16*1024*1024/4) return originalHash(data,words);
    try {
        std::vector<unsigned> copy;
        bool changed{};
        {
            std::lock_guard lock(patchesMutex);
            changed=OriginalCodeView(data,size_t(words)*4,patches,copy);
        }
        if(changed) {
            const auto value=originalHash(copy.data(),words);
            if(corrected.fetch_add(1)<8) Log("GFWL compatibility: image checksum includes known mod edits; bytes=%u",words*4);
            return value;
        }
    } catch(...) { // Allocation failure must not convert a failed check into success.
        Log("GFWL compatibility: checksum view allocation failed; using live bytes");
    }
    return originalHash(data,words);
}
unsigned __stdcall ImageHash(const void* data,unsigned words) { return HashImage(data,words,_ReturnAddress()); }
}
#ifdef DIRT2VR_GFWL_TEST
unsigned TestGfwlHash(const void* data,unsigned words,bool imageCheck) {
    return HashImage(data,words,imageCheck ? verifyCaller : nullptr);
}
#endif
MH_STATUS EnableRecordedHook(void* target) {
    if(!recording) return MH_EnableHook(target);
    // MinHook writes five entry bytes, or five padding bytes plus a two-byte
    // entry jump for its hot-patch-above case. Capture both without guessing.
    auto start=static_cast<unsigned char*>(target)-5;
    CodePatch patch;
    patch.address=reinterpret_cast<uintptr_t>(start);
    patch.before.resize(10);
    SIZE_T read{};
    if(!ReadProcessMemory(GetCurrentProcess(),start,patch.before.data(),10,&read) || read!=10) return MH_ERROR_MEMORY_PROTECT;
    patch.after.resize(10);
    std::lock_guard lock(patchesMutex);
    patches.reserve(patches.size()+1); // Allocation must happen before changing code.
    auto result=MH_EnableHook(target);
    if(result==MH_OK) {
        std::memcpy(patch.after.data(),start,10);
        patches.push_back(std::move(patch));
    }
    return result;
}
void RecordCodeByte(void* target,unsigned char before,unsigned char after) {
    if(!recording) return;
    std::lock_guard lock(patchesMutex);
    patches.push_back({reinterpret_cast<uintptr_t>(target),{before},{after}});
}
bool EnableGfwlCompatibility() {
    if(originalHash) return true;
    auto module=GetModuleHandleW(L"xlive.dll");
    if(!module || !KnownFile(module)) return true; // Existing replacement backends are unchanged.
    auto base=reinterpret_cast<unsigned char*>(module);
    const unsigned char hashEntry[]={0x8b,0xff,0x55,0x8b,0xec,0x8b,0x55,0x0c,0x8b,0x4d,0x08};
    const unsigned char caller[]={0xe8,0x34,0xd8,0xff,0xff,0x89,0x45,0xe8};
    if(std::memcmp(base+0xf0f3b,hashEntry,sizeof(hashEntry)) || std::memcmp(base+0xf3702,caller,sizeof(caller))) {
        Log("GFWL compatibility: loaded code differs from verified build; refusing hook"); return false;
    }
    HMODULE pinned{};
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
                          reinterpret_cast<LPCWSTR>(module),&pinned)) return false;
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED) return false;
    verifyCaller=base+0xf3707;
    status=MH_CreateHook(base+0xf0f3b,reinterpret_cast<void*>(ImageHash),reinterpret_cast<void**>(&originalHash));
    if(status!=MH_OK) return false;
    recording=true;
    status=EnableRecordedHook(base+0xf0f3b);
    Log("GFWL compatibility: verified 3.5.95.0, original profile APIs retained, checksum hook=%s",MH_StatusToString(status));
    return status==MH_OK;
}
}
