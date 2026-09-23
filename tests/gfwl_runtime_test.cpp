#include "gfwl_compat.h"
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <stdexcept>

namespace vr { unsigned TestGfwlHash(const void*,unsigned,bool); }
void Check(bool result,const char* text) { if(!result) throw std::runtime_error(text); }
unsigned __stdcall Replacement(unsigned value) { return value+33; }
int wmain(int argc,wchar_t**argv) {
    if(argc!=2) { puts("Supply the verified Microsoft 3.5.95.0 xlive.dll path; no game or profile is launched."); return 77; }
    auto module=LoadLibraryW(argv[1]);
    Check(module!=nullptr,"load signed DLL");
    Check(vr::EnableGfwlCompatibility(),"install guarded checksum hook");
    std::array<unsigned,32> pristine{},live{};
    for(unsigned i=0;i<pristine.size();++i) pristine[i]=i*17;
    live=pristine;
    const auto baseline=vr::TestGfwlHash(pristine.data(),32,false);
    using Hash=unsigned (__stdcall*)(const void*,unsigned);
    auto installedHash=reinterpret_cast<Hash>(reinterpret_cast<unsigned char*>(module)+0xf0f3b);
    Check(installedHash(pristine.data(),32)==baseline,"actual installed entry forwards unrelated callers");
    auto bytes=reinterpret_cast<unsigned char*>(live.data());
    auto before=bytes[7]; bytes[7]=0xee;
    vr::RecordCodeByte(bytes+7,before,0xee);
    Check(vr::TestGfwlHash(live.data(),32,false)!=baseline,"other hash callers see live bytes");
    Check(installedHash(live.data(),32)!=baseline,"installed hook limits normalization to verification caller");
    Check(vr::TestGfwlHash(live.data(),32,true)==baseline,"actual GFWL checksum recognizes original mod byte");
    bytes[30]^=0xab;
    Check(vr::TestGfwlHash(live.data(),32,true)!=baseline,"unrelated modifications remain detectable");
    bytes[30]^=0xab; bytes[7]=0xcc;
    Check(vr::TestGfwlHash(live.data(),32,true)==vr::TestGfwlHash(live.data(),32,false),"changed mod byte is not normalized");
    auto code=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE));
    Check(code!=nullptr,"allocate synthetic hook target");
    memset(code,0x90,64);
    const unsigned char body[]={0x8b,0xff,0x55,0x8b,0xec,0x8b,0x45,0x08,0x83,0xc0,0x11,0x5d,0xc2,0x04,0x00};
    memcpy(code+16,body,sizeof(body)); FlushInstructionCache(GetCurrentProcess(),code,64);
    const auto codeHash=vr::TestGfwlHash(code,16,false);
    using Function=unsigned (__stdcall*)(unsigned);
    Function original{};
    Check(MH_CreateHook(code+16,reinterpret_cast<void*>(Replacement),reinterpret_cast<void**>(&original))==MH_OK,"create real MinHook detour");
    Check(vr::EnableRecordedHook(code+16)==MH_OK,"enable recorded detour");
    Check(reinterpret_cast<Function>(code+16)(2)==35 && original(2)==19,"detour and original execute correctly");
    Check(vr::TestGfwlHash(code,16,false)!=codeHash,"unadjusted detour checksum differs");
    Check(vr::TestGfwlHash(code,16,true)==codeHash,"actual GFWL checksum accepts reconstructed detour bytes");
    Check(MH_DisableHook(code+16)==MH_OK,"disable synthetic detour");
    MH_Uninitialize(); VirtualFree(code,0,MEM_RELEASE);
    puts("Real GFWL checksum + MinHook: known edits accounted for; foreign callers and changes preserved. No game/profile operations used.");
}
