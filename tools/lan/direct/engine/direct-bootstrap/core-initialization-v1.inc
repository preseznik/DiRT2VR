// Native initial-load requests, independent of StateInitialLoad/StateGlobal.
struct NativeEvent { U32 table{},handle{}; };
static_assert(sizeof(NativeEvent)==8);
NativeEvent renderDone{},updateDone{},applicationDone{};
volatile LONG coreIssued{},coreReady{};unsigned coreObserved{};
struct PrepareMessage { U32 table,next,event,dependency; };
static_assert(sizeof(PrepareMessage)==16);
bool QueueCore(const char* channel,U32 table,U32 entry,NativeEvent& event,NativeEvent* dependency) {
    U32 strong{};
    reinterpret_cast<void(__thiscall*)(void*,const char*)>(At(0x117B460))(&strong,channel);
    if(strong<0x10000 || !(Read(strong+0x38)&0xff)) {
        if(strong) reinterpret_cast<void(__thiscall*)(void*)>(At(0x1177500))(&strong);
        Log("CORE REJECT native message channel unavailable\r\n");return false;
    }
    PrepareMessage message{At(table),0,reinterpret_cast<U32>(&event),reinterpret_cast<U32>(dependency)};
    const U32 result=reinterpret_cast<U32(__thiscall*)(void*,void*)>(At(entry))(reinterpret_cast<void*>(strong),&message);
    char line[200]{};sprintf_s(line,"CORE queued channel=%s message=%08x dispatcher=%08x result=%u event=%08x dependency=%08x\r\n",channel,table,strong,result,event.handle,dependency?dependency->handle:0);Log(line);
    reinterpret_cast<void(__thiscall*)(void*)>(At(0x1177500))(&strong);
    return result==0;
}
void BeginCoreInitialization(U32 frontend) {
    if(InterlockedCompareExchange(&coreIssued,1,0)) return;
    const U32 allocator=Read(frontend+0xC);
    NativeAllocationScope scope(allocator);
    if(!scope.Valid(allocator)) {InterlockedExchange(&holdFailed,1);Log("CORE REJECT allocation scope\r\n");return;}
    reinterpret_cast<void(__thiscall*)(void*)>(At(0x1159780))(&renderDone);
    // Shared update completion is manual-reset; render preparation also waits on it.
    reinterpret_cast<void(__thiscall*)(void*,U32,U32)>(At(0x11597A0))(&updateDone,0,0);
    reinterpret_cast<void(__thiscall*)(void*)>(At(0x1159780))(&applicationDone);
    if(!renderDone.handle || !updateDone.handle || !applicationDone.handle ||
       !QueueCore("update_task",0x1313C44,0x6626A0,updateDone,nullptr) ||
       !QueueCore("render_task",0x1313C78,0x662740,renderDone,&updateDone) ||
       !QueueCore("application_object",0x1313BB4,0x6627E0,applicationDone,nullptr)) {
        InterlockedExchange(&holdFailed,1);Log("CORE REJECT initial preparation request\r\n");
    }
    // Event wrappers remain pinned until process exit; asynchronous owners can
    // retain them. Do not destroy them or retry a partially queued transaction.
}
bool CoreInitializationReady() {
    if(!coreIssued || holdFailed) return false;
    if(coreReady) return true;
    NativeEvent* events[]{&renderDone,&updateDone,&applicationDone};
    for(unsigned i=0;i<3;++i) {
        const unsigned bit=1u<<i;
        if(coreObserved&bit) continue;
        const DWORD status=WaitForSingleObject(reinterpret_cast<HANDLE>(events[i]->handle),0);
        if(status==WAIT_OBJECT_0) {coreObserved|=bit;char line[100]{};sprintf_s(line,"CORE completed event=%u observed=%u\r\n",i,coreObserved);Log(line);}
        else if(status!=WAIT_TIMEOUT) {InterlockedExchange(&holdFailed,1);Log("CORE REJECT completion event\r\n");return false;}
    }
    if(coreObserved==7) {InterlockedExchange(&coreReady,1);Log("CORE native initialization complete; frontend flow remains inactive\r\n");}
    return coreReady!=0;
}
bool CoreSignatures() {
    constexpr unsigned char channel[]{0x8b,0x44,0x24,0x04,0x8b,0x15,0x58,0x00,0x6a,0x01};
    constexpr unsigned char event[]{0x56,0x6a,0x00,0x6a,0x00,0x6a,0x00,0x8b,0xf1};
    constexpr unsigned char queue[]{0x56,0x8b,0xf1,0x80,0x7e,0x38,0x00};
    // The resolver's absolute operand is ASLR-relocated; validate its complete
    // non-address prefix here. Exact supported executable hash is checked too.
    return trace::ReadableEqual(reinterpret_cast<void*>(At(0x117B460)),channel,6) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x1159780)),event,sizeof(event)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x6626A0)),queue,sizeof(queue)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x662740)),queue,sizeof(queue)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x6627E0)),queue,sizeof(queue));
}
