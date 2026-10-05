#include "profile_names.h"
#include <cstdint>
#include <cstring>
#include <string_view>

namespace {
using GetField=const unsigned char* (__thiscall*)(void*,const char*);
GetField getField{};

std::string_view Field(void* profile,const char* key) {
    const auto field=getField(profile,key);
    if (!field) return {};
    const auto text=reinterpret_cast<const char*>(field+8);
    return {text,strnlen_s(text,128)};
}

int DisplayName(void* profile,char* output,unsigned size,bool surname) {
    if (!output || !size) return -1;
    const auto first=Field(profile,"Player_FirstName");
    const auto last=Field(profile,"Player_LastName");
    unsigned written=0;
    bool truncated=false;
    const auto append=[&](std::string_view text) {
        for (const char c:text) {
            if (written+1>=size) { truncated=true; break; }
            output[written++]=c;
        }
    };
    if (surname) append(last.empty() ? first : last);
    else {
        append(first);
        if (!first.empty() && !last.empty()) append(" ");
        append(last);
    }
    output[written]=0;
    return truncated ? -1 : static_cast<int>(written);
}

int __fastcall FullName(void* profile,void*,char* output,unsigned size) { return DisplayName(profile,output,size,false); }
int __fastcall LastName(void* profile,void*,char* output,unsigned size) { return DisplayName(profile,output,size,true); }
}

bool InstallManagedProfileNames(HMODULE host) {
    auto base=reinterpret_cast<unsigned char*>(host);
    if (!base) return false;
    // These are display-only methods. Never populate a missing surname in the save:
    // that would duplicate the name on screens which display both fields.
    constexpr unsigned char fullStart[]{0x53,0x56,0x57,0x68};
    constexpr unsigned char getterStart[]{0x53,0x56,0x57,0x8b,0xf9,0x8b,0x77,0x24};
    std::uintptr_t firstKey{},lastKey{};
    std::memcpy(&firstKey,base+0x2c5514,4);
    std::memcpy(&lastKey,base+0x2c55c1,4);
    if (std::memcmp(base+0x2c5510,fullStart,sizeof(fullStart)) || base[0x2c55c0]!=0x68 ||
        firstKey!=reinterpret_cast<std::uintptr_t>(base+0xf0ad00) ||
        lastKey!=reinterpret_cast<std::uintptr_t>(base+0xf1b470) ||
        std::memcmp(base+0x808500,getterStart,sizeof(getterStart))) return false;
    DWORD fullProtection{},lastProtection{},ignored{};
    if (!VirtualProtect(base+0x2c5510,5,PAGE_EXECUTE_READWRITE,&fullProtection)) return false;
    if (!VirtualProtect(base+0x2c55c0,5,PAGE_EXECUTE_READWRITE,&lastProtection)) {
        VirtualProtect(base+0x2c5510,5,fullProtection,&ignored);
        return false;
    }
    getField=reinterpret_cast<GetField>(base+0x808500);
    const auto jump=[&](unsigned rva,auto target) {
        const auto displacement=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(target)-reinterpret_cast<std::uintptr_t>(base+rva+5));
        base[rva]=0xe9;
        std::memcpy(base+rva+1,&displacement,4);
    };
    jump(0x2c5510,&FullName);
    jump(0x2c55c0,&LastName);
    const bool fullRestored=VirtualProtect(base+0x2c5510,5,fullProtection,&ignored)!=FALSE;
    const bool lastRestored=VirtualProtect(base+0x2c55c0,5,lastProtection,&ignored)!=FALSE;
    const bool flushed=FlushInstructionCache(GetCurrentProcess(),nullptr,0)!=FALSE;
    return fullRestored && lastRestored && flushed;
}
