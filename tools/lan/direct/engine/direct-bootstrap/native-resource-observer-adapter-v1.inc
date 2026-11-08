// Observe native completion under its own lock, before native job destruction.
// No native object survives in the receipt ledger. Module stays process-pinned.
namespace native_resource_observer_v1 {
#include "../resource-completion-20261018/native-resource-observer-windows-v1.cpp"
}
namespace resource_observer_adapter_v1 {
using NoArgs=void(__thiscall*)(void*);
bool attempted{},installed{};
U32 managerIdentity{};
bool Healthy(U32 manager) {
    if(!installed || managerIdentity!=manager || Read(At(0x15D9A04))!=manager ||
       Read(manager)!=At(0x135B85C) || Read(manager+0x58)!=manager+0x5C)return false;
    bool locked=false,result=false;
    __try {
        reinterpret_cast<NoArgs>(At(0x11588C0))(reinterpret_cast<void*>(manager+0x58));locked=true;
        const auto* receipt=native_resource_observer_v1::ResourceObserverLedgerUnderNativeLock();
        result=receipt->armed && receipt->base==base && receipt->manager==manager &&
            receipt->fault==native_resource_observer_v1::resource_completion::Fault::None;
    } __finally {if(locked)reinterpret_cast<NoArgs>(At(0x11588D0))(reinterpret_cast<void*>(manager+0x58));}
    return result;
}
bool Install(U32 manager) {
    if(attempted)return installed && managerIdentity==manager;
    attempted=true;managerIdentity=manager;
    if(Read(At(0x15D9A04))!=manager || Read(manager)!=At(0x135B85C) || Read(manager+0x58)!=manager+0x5C)return false;
    bool locked=false,result=false;
    __try {
        reinterpret_cast<NoArgs>(At(0x11588C0))(reinterpret_cast<void*>(manager+0x58));locked=true;
        // Every audited route to C28490 holds this same lock. No caller can be
        // executing this CALL while we own it; no thread suspension is needed.
        if(!native_resource_observer_v1::ConfigureResourceObserverUnderNativeLock(base,manager))return false;
        unsigned char patch[]{0xE8,0,0,0,0};
        const U32 hook=reinterpret_cast<U32>(&native_resource_observer_v1::ResourceRetireCallsiteObserver);
        const U32 displacement=hook-At(0xC28495);memcpy(patch+1,&displacement,4);
        DWORD previous{};
        void* target=reinterpret_cast<void*>(At(0xC28490));
        if(!VirtualProtect(target,sizeof(patch),PAGE_EXECUTE_READWRITE,&previous))return false;
        bool restored=false;
        __try {memcpy(target,patch,sizeof(patch));}
        __finally {DWORD ignored{};restored=VirtualProtect(target,sizeof(patch),previous,&ignored)!=0;}
        result=restored && FlushInstructionCache(GetCurrentProcess(),target,sizeof(patch)) &&
            native_resource_observer_v1::resource_completion::Signatures(native_resource_observer_v1::Equal,base,hook);
        installed=result;
    } __finally {if(locked)reinterpret_cast<NoArgs>(At(0x11588D0))(reinterpret_cast<void*>(manager+0x58));}
    char text[180]{};sprintf_s(text,"RESOURCE_OBSERVER installed=%u manager=%08x callsite=%08x no_native_calls_in_observation=1\r\n",unsigned(result),manager,At(0xC28490));Log(text);
    return result;
}
}
