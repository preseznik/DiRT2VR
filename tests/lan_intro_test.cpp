#include "intro.h"
#include <cstdint>
#include <cstring>
#include <iostream>

int main() {
    // An allocated stand-in image exercises relocation, guards and page-protection handling.
    auto base=static_cast<std::uint8_t*>(VirtualAlloc(nullptr,0x300000,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    if (!base) return 1;
    auto factory=base+0x284115;
    auto decision=base+0x225e73;
    auto original=reinterpret_cast<std::uintptr_t>(base+0x276050);
    factory[0]=0xbe;
    std::memcpy(factory+1,&original,sizeof(original));
    decision[0]=0x74; decision[1]=0x14; decision[2]=0x68;
    auto host=reinterpret_cast<HMODULE>(base);
    if (InstallLanIntroSkip(nullptr) || InstallLanIntroSkip(host)) return 2;
    std::uintptr_t actual{};
    std::memcpy(&actual,factory+1,sizeof(actual));
    if (actual!=original) return 3; // A bad second site must not partially patch the first.
    decision[0]=0x75;
    DWORD old{};
    if (!VirtualProtect(base,0x300000,PAGE_EXECUTE_READ,&old)) return 4;
    if (!InstallLanIntroSkip(host) || InstallLanIntroSkip(host)) return 5;
    std::memcpy(&actual,factory+1,sizeof(actual));
    if (actual!=reinterpret_cast<std::uintptr_t>(base+0xdb440) || decision[0]!=0xeb) return 6;
    MEMORY_BASIC_INFORMATION info{};
    if (!VirtualQuery(factory,&info,sizeof(info)) || info.Protect!=PAGE_EXECUTE_READ) return 7;
    if (!VirtualQuery(decision,&info,sizeof(info)) || info.Protect!=PAGE_EXECUTE_READ) return 8;
    VirtualFree(base,0,MEM_RELEASE);
    std::cout << "Intro guards, relocation, rejection without partial writes and page protection passed\n";
}
