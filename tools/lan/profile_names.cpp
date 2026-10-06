#include "profile_names.h"
#include <cstdint>
#include <cstring>
#include <string_view>

namespace {
using GetField=const unsigned char* (__thiscall*)(void*,const char*);
GetField getField{};
using StringName=void (__thiscall*)(void*,void*);
using ShortStringName=void (__thiscall*)(void*,void*,unsigned);
using ShortBufferName=int (__thiscall*)(void*,char*,unsigned,unsigned);
StringName firstStringName{},fullStringName{},originalLastStringName{};
ShortStringName originalShortStringName{};
ShortBufferName originalShortBufferName{};

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

// The game's abbreviated formatters emit nothing when only a first name exists.
// Keep their original abbreviation rules for two-part names; use the existing
// full-name formatter for a one-part name, without adding a surname to the save.
bool FirstNameOnly(void* profile) { return !Field(profile,"Player_FirstName").empty() && Field(profile,"Player_LastName").empty(); }
void __fastcall ShortStringNameFixed(void* profile,void*,void* output,unsigned limit) {
    if (FirstNameOnly(profile)) fullStringName(profile,output);
    else originalShortStringName(profile,output,limit);
}
int __fastcall ShortBufferNameFixed(void* profile,void*,char* output,unsigned size,unsigned limit) {
    return FirstNameOnly(profile) ? DisplayName(profile,output,size,false) : originalShortBufferName(profile,output,size,limit);
}
void __fastcall LastStringNameFixed(void* profile,void*,void* output) {
    if (Field(profile,"Player_LastName").empty()) firstStringName(profile,output);
    else originalLastStringName(profile,output);
}

void Jump(unsigned char* from,const void* target) {
    const auto displacement=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(target)-reinterpret_cast<std::uintptr_t>(from+5));
    from[0]=0xe9;
    std::memcpy(from+1,&displacement,4);
}
}

bool InstallManagedProfileNames(HMODULE host) {
    auto base=reinterpret_cast<unsigned char*>(host);
    if (!base) return false;
    // These are display-only methods. Never populate a missing surname in the save:
    // that would duplicate the name on screens which display both fields.
    constexpr unsigned char fullStart[]{0x53,0x56,0x57,0x68};
    constexpr unsigned char getterStart[]{0x53,0x56,0x57,0x8b,0xf9,0x8b,0x77,0x24};
    constexpr unsigned char shortStringStart[]{0x83,0xec,0x10,0x53,0x55,0x56,0x57,0x68};
    constexpr unsigned char shortBufferStart[]{0x83,0xec,0x10,0x55,0x56,0x57,0x68};
    constexpr unsigned char lastStringStart[]{0x56,0x57,0x68};
    std::uintptr_t firstKey{},lastKey{};
    std::memcpy(&firstKey,base+0x2c5514,4);
    std::memcpy(&lastKey,base+0x2c55c1,4);
    if (std::memcmp(base+0x2c5510,fullStart,sizeof(fullStart)) || base[0x2c55c0]!=0x68 ||
        firstKey!=reinterpret_cast<std::uintptr_t>(base+0xf0ad00) ||
        lastKey!=reinterpret_cast<std::uintptr_t>(base+0xf1b470) ||
        std::memcmp(base+0x808500,getterStart,sizeof(getterStart)) ||
        std::memcmp(base+0x2ce2c0,shortStringStart,sizeof(shortStringStart)) ||
        std::memcmp(base+0x2ce3c0,shortBufferStart,sizeof(shortBufferStart)) ||
        std::memcmp(base+0x2ce510,lastStringStart,sizeof(lastStringStart)) ||
        std::memcmp(base+0x2ce2c8,&firstKey,4) || std::memcmp(base+0x2ce3c7,&firstKey,4) ||
        std::memcmp(base+0x2ce513,&lastKey,4) ||
        std::memcmp(base+0x2ce210,fullStart,sizeof(fullStart)) || std::memcmp(base+0x2ce214,&firstKey,4) ||
        std::memcmp(base+0x2ce4c0,lastStringStart,sizeof(lastStringStart)) || std::memcmp(base+0x2ce4c3,&firstKey,4)) return false;

    // Copy only complete, position-independent entry instructions. The third
    // entry includes an already-rebased absolute string address, not a rel32.
    auto trampolines=static_cast<unsigned char*>(VirtualAlloc(nullptr,64,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    if (!trampolines) return false;
    std::memcpy(trampolines,base+0x2ce2c0,7); Jump(trampolines+7,base+0x2ce2c7);
    std::memcpy(trampolines+16,base+0x2ce3c0,6); Jump(trampolines+22,base+0x2ce3c6);
    std::memcpy(trampolines+32,base+0x2ce510,7); Jump(trampolines+39,base+0x2ce517);
    DWORD fullProtection{},stringProtection{},ignored{};
    if (!VirtualProtect(trampolines,64,PAGE_EXECUTE_READ,&ignored) ||
        !VirtualProtect(base+0x2c5510,0xb5,PAGE_EXECUTE_READWRITE,&fullProtection)) {
        VirtualFree(trampolines,0,MEM_RELEASE);
        return false;
    }
    // Each range is within one code page; protect/restore it once, even where
    // several entry points share the page.
    if (!VirtualProtect(base+0x2ce2c0,0x255,PAGE_EXECUTE_READWRITE,&stringProtection)) {
        VirtualProtect(base+0x2c5510,0xb5,fullProtection,&ignored);
        VirtualFree(trampolines,0,MEM_RELEASE);
        return false;
    }
    getField=reinterpret_cast<GetField>(base+0x808500);
    firstStringName=reinterpret_cast<StringName>(base+0x2ce4c0);
    fullStringName=reinterpret_cast<StringName>(base+0x2ce210);
    originalShortStringName=reinterpret_cast<ShortStringName>(trampolines);
    originalShortBufferName=reinterpret_cast<ShortBufferName>(trampolines+16);
    originalLastStringName=reinterpret_cast<StringName>(trampolines+32);
    Jump(base+0x2c5510,&FullName);
    Jump(base+0x2c55c0,&LastName);
    Jump(base+0x2ce2c0,&ShortStringNameFixed);
    Jump(base+0x2ce3c0,&ShortBufferNameFixed);
    Jump(base+0x2ce510,&LastStringNameFixed);
    const bool fullRestored=VirtualProtect(base+0x2c5510,0xb5,fullProtection,&ignored)!=FALSE;
    const bool lastRestored=VirtualProtect(base+0x2ce2c0,0x255,stringProtection,&ignored)!=FALSE;
    const bool flushed=FlushInstructionCache(GetCurrentProcess(),nullptr,0)!=FALSE;
    return fullRestored && lastRestored && flushed;
}
