#include "driving_controls.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <array>
#include <cstring>
#include <fstream>
#include <map>
#include <mutex>
#include <string>


namespace vr {
namespace {
using Reader=std::array<unsigned,11>;
using ParseFn=unsigned (__thiscall*)(void*,void*,void*,void*,unsigned,unsigned,unsigned);
using AttributeFn=const char* (__thiscall*)(void*,const char*);
ParseFn original{};
unsigned char* base{};
std::filesystem::path bindingsPath;
bool diagnostic{};
std::map<std::string,Reader> overrides;
std::once_flag loaded;
std::array<void*,2> document{}; // Kept alive while the game retains parsed string references.
std::string xml;
template<class T> T Function(unsigned rva) { return reinterpret_cast<T>(base+rva); }
bool Valid(const Reader& r) { return r[10] || (r[8] && r[1]<r[6]); }
void Load() {
    if(bindingsPath.empty()) return;
    std::ifstream input(bindingsPath,std::ios::binary);
    xml.assign(std::istreambuf_iterator<char>(input),{});
    if(xml.empty() || xml.size()>262144) { Log("driving controls: invalid configuration size"); ExitProcess(ERROR_INVALID_DATA); }
    Function<void (__thiscall*)(void*)>(0xaf3740)(document.data());
    if(!Function<bool (__thiscall*)(void*,const char*,unsigned)>(0xb01a80)(document.data(),xml.data(),static_cast<unsigned>(xml.size()))) {
        Log("driving controls: XML parse failed"); ExitProcess(ERROR_INVALID_DATA);
    }
    Reader row{};
    auto root=document[0];
    auto children=reinterpret_cast<void* (__thiscall*)(void*,void*,const char*)>((*static_cast<void***>(root))[1]);
    children(root,row.data(),"Action");
    for(unsigned count=0;Valid(row)&&count<64;++count) {
        const char* action=Function<AttributeFn>(0xaf39e0)(row.data(),"actionName");
        if(!action || !*action || !overrides.emplace(action,row).second) { Log("driving controls: invalid/duplicate action"); ExitProcess(ERROR_INVALID_DATA); }
        Function<void (__thiscall*)(void*)>(0xaf82e0)(row.data());
    }
    if(Valid(row)||overrides.empty()) { Log("driving controls: invalid action count"); ExitProcess(ERROR_INVALID_DATA); }
    Log("driving controls: loaded %zu action overrides",overrides.size());
}
unsigned __fastcall Parse(void* self,void*,void* reader,void* dictionary,void* devices,unsigned a,unsigned b,unsigned c) {
    std::call_once(loaded,Load);
    const char* name=Function<AttributeFn>(0xaf39e0)(reader,"actionName");
    if(name) {
        auto found=overrides.find(name);
        if(found!=overrides.end()) reader=found->second.data();
        if(diagnostic) {
            Reader axis{};
            Function<void* (__thiscall*)(void*,void*,const char*)>(0xafd490)(reader,axis.data(),"Axis");
            unsigned count=0;
            while(Valid(axis)&&count++<8) {
                const char* device=Function<AttributeFn>(0xaf39e0)(axis.data(),"deviceName");
                const char* control=Function<AttributeFn>(0xaf39e0)(axis.data(),"axisName");
                Log("driving action=%s device=%s control=%s overridden=%d",name,device?device:"",control?control:"",found!=overrides.end());
                Function<void (__thiscall*)(void*)>(0xaf82e0)(axis.data());
            }
        }
    }
    const auto result=original(self,reader,dictionary,devices,a,b,c);
    if(diagnostic && name && (overrides.contains(name) || strstr(name,"Gear") || strcmp(name,"Clutch")==0)) Log("driving parse result action=%s result=%u",name,result);
    return result;
}
}
bool EnableDrivingControls() {
    if(original) return true;
    wchar_t path[32768]{},trace[8]{};
    const auto length=GetEnvironmentVariableW(L"DIRT2VR_DRIVING_CONTROLS",path,32768);
    diagnostic=GetEnvironmentVariableW(L"DIRT2VR_TRACE_CONTROLS",trace,8)==1&&trace[0]==L'1';
    if(!length&&!diagnostic) return true;
    if(length>=32768||!SupportedHost()) return false;
    if(length && !EnableGfwlCompatibility()) return false;
    bindingsPath=path;
    base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char guard[]={0x83,0xec,0x38,0x55,0x56,0x8b,0xe9,0x57};
    if(std::memcmp(base+0xb095b0,guard,sizeof(guard))) return false;
    auto status=MH_Initialize();
    if(status==MH_OK||status==MH_ERROR_ALREADY_INITIALIZED) {
        status=MH_CreateHook(base+0xb095b0,reinterpret_cast<void*>(Parse),reinterpret_cast<void**>(&original));
        if(status==MH_OK) status=EnableRecordedHook(base+0xb095b0);
    }
    Log("driving controls: parser hook=%s",MH_StatusToString(status));
    return status==MH_OK;
}
}
