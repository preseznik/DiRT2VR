#define DIRECTINPUT_VERSION 0x0800
#include "seat_adjustment.h"
#include "test_message.h"
#include "gfwl_compat.h"
#include "common.h"
#include <dinput.h>
#include <xinput.h>
#include <MinHook.h>
#include <map>
#include <mutex>
#include <vector>
#include <intrin.h>

namespace vr {
namespace {
struct Device { GUID id{}; bool keyboard{},unicode{}; std::vector<DIOBJECTDATAFORMAT> objects; };
std::mutex deviceMutex;
std::map<void*,Device> devices;
using CreateFn=HRESULT (STDMETHODCALLTYPE*)(IDirectInput8W*,REFGUID,IDirectInputDevice8W**,IUnknown*);
using StateFn=HRESULT (STDMETHODCALLTYPE*)(IDirectInputDevice8W*,DWORD,void*);
using DataFn=HRESULT (STDMETHODCALLTYPE*)(IDirectInputDevice8W*,DWORD,DIDEVICEOBJECTDATA*,DWORD*,DWORD);
using FormatFn=HRESULT (STDMETHODCALLTYPE*)(IDirectInputDevice8W*,const DIDATAFORMAT*);
CreateFn create{},createA{}; StateFn state{}; DataFn data{}; FormatFn format{};
using AsyncFn=SHORT (WINAPI*)(int);
AsyncFn asyncKey{};
bool GameCaller(void* caller) {
    static auto base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    static auto size=reinterpret_cast<IMAGE_NT_HEADERS*>(base+reinterpret_cast<IMAGE_DOS_HEADER*>(base)->e_lfanew)->OptionalHeader.SizeOfImage;
    auto address=reinterpret_cast<uintptr_t>(caller); return address>=base && address<base+size;
}
SHORT WINAPI AsyncKey(int key) {
    auto value=asyncKey(key);
    return GameCaller(_ReturnAddress()) && (TestMessageBlocksKey(unsigned(key)) || SeatBlocksKey(unsigned(key))) ? 0 : value;
}
void FilterInput(Device& device,DWORD offset,DWORD type,void* value,DWORD size) {
    if(device.keyboard && size==1) {
        const auto scan=DIDFT_GETINSTANCE(type);
        const auto code=(scan&0x80)?0xe000u|(scan&0x7f):scan;
        const auto key=MapVirtualKeyW(code,MAPVK_VSC_TO_VK_EX);
        if(TestMessageBlocksKey(key)){*static_cast<BYTE*>(value)=0;return;}
    }
    SeatFilterController(device.id,device.keyboard,offset,type,value,size);
}
void Filter(Device& device,DWORD offset,void* value,DWORD bytes) {
    for(const auto& object:device.objects) {
        const DWORD size=(object.dwType&DIDFT_BUTTON)?1:4;
        if(offset==object.dwOfs && bytes>=size) { FilterInput(device,offset,object.dwType,value,size); break; }
    }
}
HRESULT STDMETHODCALLTYPE State(IDirectInputDevice8W* self,DWORD length,void* output) {
    const auto hr=state(self,length,output);
    if(SUCCEEDED(hr) && output) {
        std::lock_guard lock(deviceMutex);
        if(auto it=devices.find(self);it!=devices.end()) for(const auto& object:it->second.objects) {
            const unsigned size=(object.dwType&DIDFT_BUTTON)?1:4;
            if(object.dwOfs<=length && size<=length-object.dwOfs)
                FilterInput(it->second,object.dwOfs,object.dwType,static_cast<BYTE*>(output)+object.dwOfs,size);
        }
    }
    return hr;
}
HRESULT STDMETHODCALLTYPE Data(IDirectInputDevice8W* self,DWORD objectSize,DIDEVICEOBJECTDATA* output,DWORD* count,DWORD flags) {
    const auto hr=data(self,objectSize,output,count,flags);
    if(SUCCEEDED(hr) && output && count && objectSize>=16) {
        std::lock_guard lock(deviceMutex);
        if(auto it=devices.find(self);it!=devices.end()) for(DWORD i=0;i<*count;++i) {
            auto row=reinterpret_cast<DIDEVICEOBJECTDATA*>(reinterpret_cast<BYTE*>(output)+i*objectSize);
            Filter(it->second,row->dwOfs,&row->dwData,sizeof(row->dwData));
        }
    }
    return hr;
}
HRESULT STDMETHODCALLTYPE Format(IDirectInputDevice8W* self,const DIDATAFORMAT* value) {
    const auto hr=format(self,value);
    if(SUCCEEDED(hr) && value && value->dwObjSize==sizeof(DIOBJECTDATAFORMAT) && value->dwNumObjs<=512) {
        std::vector<DIOBJECTDATAFORMAT> objects(value->rgodf,value->rgodf+value->dwNumObjs);
        bool unicode=false;
        { std::lock_guard lock(deviceMutex); unicode=devices[self].unicode; }
        for(auto& object:objects) {
            if(unicode) {
                DIDEVICEOBJECTINSTANCEW info{sizeof(info)};
                if(SUCCEEDED(self->GetObjectInfo(&info,object.dwOfs,DIPH_BYOFFSET))) object.dwType=info.dwType;
            } else {
                DIDEVICEOBJECTINSTANCEA info{sizeof(info)};
                if(SUCCEEDED(reinterpret_cast<IDirectInputDevice8A*>(self)->GetObjectInfo(&info,object.dwOfs,DIPH_BYOFFSET))) object.dwType=info.dwType;
            }
        }
        std::lock_guard lock(deviceMutex);
        devices[self].objects=std::move(objects);
    }
    return hr;
}
bool Hook(void* target,void* replacement,void** original) {
    auto result=MH_CreateHook(target,replacement,original);
    if(result==MH_OK) result=EnableRecordedHook(target);
    return result==MH_OK;
}
bool deviceHooks{};
template<bool Unicode> HRESULT STDMETHODCALLTYPE CreateDevice(IDirectInput8W* self,REFGUID id,IDirectInputDevice8W** out,IUnknown* outer) {
    const auto hr=(Unicode?create:createA)(self,id,out,outer);
    if(SUCCEEDED(hr) && out && *out) {
        GUID instance=id; bool keyboard=id==GUID_SysKeyboard;
        if constexpr(Unicode) {
            DIDEVICEINSTANCEW info{sizeof(info)};
            if(SUCCEEDED((*out)->GetDeviceInfo(&info))) { instance=info.guidInstance; keyboard=GET_DIDEVICE_TYPE(info.dwDevType)==DI8DEVTYPE_KEYBOARD; }
        } else {
            DIDEVICEINSTANCEA info{sizeof(info)};
            if(SUCCEEDED(reinterpret_cast<IDirectInputDevice8A*>(*out)->GetDeviceInfo(&info))) { instance=info.guidInstance; keyboard=GET_DIDEVICE_TYPE(info.dwDevType)==DI8DEVTYPE_KEYBOARD; }
        }
        {
            std::lock_guard lock(deviceMutex);
            devices[*out]={instance,keyboard,Unicode,{}};
            if(!deviceHooks) {
                auto vtable=*reinterpret_cast<void***>(*out);
                deviceHooks=Hook(vtable[9],reinterpret_cast<void*>(State),reinterpret_cast<void**>(&state)) &&
                    Hook(vtable[10],reinterpret_cast<void*>(Data),reinterpret_cast<void**>(&data)) &&
                    Hook(vtable[11],reinterpret_cast<void*>(Format),reinterpret_cast<void**>(&format));
            }
        }
    }
    return hr;
}
using XboxFn=DWORD (WINAPI*)(DWORD,XINPUT_STATE*);
XboxFn xbox[4]{};
template<unsigned I> DWORD WINAPI Xbox(DWORD slot,XINPUT_STATE* output) {
    const auto result=xbox[I](slot,output); if(result==ERROR_SUCCESS && output) SeatFilterXbox(slot,output->Gamepad.wButtons); return result;
}
}
bool EnableSeatInput() {
    static bool attempted{},ready{};
    if(attempted)return ready;
    attempted=true;
    auto module=LoadLibraryExW(L"dinput8.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!module) return false;
    using Factory=HRESULT (WINAPI*)(HINSTANCE,DWORD,REFIID,void**,IUnknown*);
    auto factory=reinterpret_cast<Factory>(GetProcAddress(module,"DirectInput8Create"));
    IDirectInput8W* di{};
    if(!factory || FAILED(factory(GetModuleHandleW(nullptr),DIRECTINPUT_VERSION,IID_IDirectInput8W,reinterpret_cast<void**>(&di),nullptr))) return false;
    auto table=*reinterpret_cast<void***>(di);
    bool ok=Hook(table[3],reinterpret_cast<void*>(CreateDevice<true>),reinterpret_cast<void**>(&create));
    IDirectInputDevice8W* keyboard{};
    if(ok && SUCCEEDED(di->CreateDevice(GUID_SysKeyboard,&keyboard,nullptr))) { keyboard->Release(); }
    di->Release(); ok=ok && deviceHooks;
    IDirectInput8A* ansi{};
    if(SUCCEEDED(factory(GetModuleHandleW(nullptr),DIRECTINPUT_VERSION,IID_IDirectInput8A,reinterpret_cast<void**>(&ansi),nullptr))) {
        auto ansiTable=*reinterpret_cast<void***>(ansi);
        ok=Hook(ansiTable[3],reinterpret_cast<void*>(CreateDevice<false>),reinterpret_cast<void**>(&createA)) && ok;
        ansi->Release();
    } else ok=false;
    auto user=GetModuleHandleW(L"user32.dll");
    ok=Hook(reinterpret_cast<void*>(GetProcAddress(user,"GetAsyncKeyState")),reinterpret_cast<void*>(AsyncKey),reinterpret_cast<void**>(&asyncKey)) && ok;
    const wchar_t* libraries[]={L"xinput1_3.dll",L"xinput1_4.dll",L"xinput9_1_0.dll",L"xinput1_2.dll"};
    void* replacements[]={reinterpret_cast<void*>(Xbox<0>),reinterpret_cast<void*>(Xbox<1>),reinterpret_cast<void*>(Xbox<2>),reinterpret_cast<void*>(Xbox<3>)};
    std::vector<void*> targets;
    for(unsigned i=0;i<4;++i) if(auto library=LoadLibraryExW(libraries[i],nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32)) {
        auto target=reinterpret_cast<void*>(GetProcAddress(library,"XInputGetState"));
        if(target && std::find(targets.begin(),targets.end(),target)==targets.end()) {
            ok=Hook(target,replacements[i],reinterpret_cast<void**>(&xbox[i])) && ok; targets.push_back(target);
        }
    }
    ready=ok;return ready;
}
}
