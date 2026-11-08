// Include inside finalization_v2 after BoundOwners/CaptureMailboxes. Compile-only.
// Sole supported sampler: actual 1351E90 -> B596F0 -> B25D50 zero-argument no-op.
enum class HintResult : unsigned { Absent, Busy, Submitted, Unsupported, Invalid };
struct HintReport {HintResult result{HintResult::Invalid};const char* reason{"unattempted"};U32 worker{},object{},storage{},nativeCode{};};
HintReport hintReport;
bool HintScope() {
    U32 frame{};
    return state.phase==Phase::AwaitTrackPulse && state.thread==GetCurrentThreadId() &&
        HeldEngine(state.game) && BoundOwners(false) &&
        initial_load_v2::NativeFrame(initial_load_v2::ledger.allocator,frame) && frame==initial_load_v2::ledger.tickFrame;
}
HintReport OptionalStreamingHint() {
    using initial_load_v2::Ptr;using initial_load_v2::Bytes;
    HintReport out{};
    const auto finish=[&](HintResult result,const char* reason){
        out.result=result;out.reason=reason;
        if(!HintScope()){out.result=HintResult::Invalid;out.reason="current native scope changed";}
        return out;
    };
    const auto equal=[](U32 address,const void* expected,size_t size){return trace::ReadableEqual(reinterpret_cast<void*>(address),expected,size);};
    if(!HintScope() || !subroute_guards_v1::Signatures(equal,base))return out;
    const U32 plugin=At(0x15CE83C);
    if(!Ptr(plugin,0x9C) || Read(plugin)!=At(0x13385FC))return out;
    out.worker=Read(plugin+0x98);
    // Original D4CCA0 takes RET4 immediately. No matrix is consumed or retained.
    // The optional hint has no finalization-success dependency in 6E0370.
    if(!out.worker) {
        HintReport absent=finish(HintResult::Absent,"streaming worker absent; native no-op");
        if(Read(plugin)!=At(0x13385FC) || Read(plugin+0x98)) {absent.result=HintResult::Invalid;absent.reason="absent worker identity changed";}
        return absent;
    }
    const auto sameWorker=[&](){
        return Read(plugin)==At(0x13385FC) && Read(plugin+0x98)==out.worker &&
            Ptr(out.worker,0x170) && !(out.worker&15) && Read(out.worker)==At(0x1362448) &&
            Read(out.worker+0x1C)==At(0x1362460) && Read(out.worker+0x6C)==At(0x13C88A8) &&
            Read(out.worker+0x74)==At(0x13C88A8) && Read(out.worker+0x70) && Read(out.worker+0x78);
    };
    if(!sameWorker())return finish(HintResult::Invalid,"streaming worker/event ownership");
    if(Read(out.worker+0x30)!=2)return finish(HintResult::Unsupported,"streaming worker not in active state2; no dispatch");
    if(!Ptr(state.routeOwner,0x1C))return finish(HintResult::Invalid,"route owner unreadable");
    U32 expectedRoute{};
    for(unsigned i=0;i<2;++i) {
        const U32 item=Read(state.routeOwner+0x14+i*4);if(!item)continue;
        char name[32]{};
        if(!Ptr(item,0x24) || !Bytes(item,name,sizeof(name)) || !memchr(name,0,sizeof(name)))return finish(HintResult::Invalid,"route record/name invalid");
        if(strcmp(name,"subroute_0"))continue;
        const U32 route=Read(item+0x20);if(!route)continue;
        if(expectedRoute && route!=expectedRoute)return finish(HintResult::Unsupported,"ambiguous subroute; no dispatch");
        expectedRoute=route;
    }
    const U32 route=reinterpret_cast<U32(__thiscall*)(void*,const char*)>(At(0xD18D60))(reinterpret_cast<void*>(state.routeOwner),"subroute_0");
    if(route!=expectedRoute)return finish(HintResult::Invalid,"native route lookup disagrees");
    if(!route)return finish(HintResult::Absent,"subroute absent");
    if(!Ptr(route,0x1CC))return finish(HintResult::Invalid,"route container unreadable");
    const U32 container=route+0x1C0,begin=Read(container+4),end=Read(container+8);
    U32 count{};
    if(!native_subroute_v1::RecordCount(begin,end,count) || (count && !Ptr(begin,end-begin)))return finish(HintResult::Invalid,"animation record bounds");
    if(!count)return finish(HintResult::Absent,"intro collection empty");
    U32 objects[native_subroute_v1::kDiagnosticRecordLimit]{};
    for(U32 i=0;i<count;++i) {
        objects[i]=Read(begin+i*0x270);char name[64]{};
        if(!Ptr(objects[i],0x124) || !Bytes(objects[i]+0x80,name,sizeof(name)) || !memchr(name,0,sizeof(name)))return finish(HintResult::Unsupported,"animation name not bounded; no resolver call");
    }
    out.object=reinterpret_cast<U32(__cdecl*)(void*,U32*)>(At(0x6CBF30))(reinterpret_cast<void*>(container),&out.storage);
    if(!HintScope() || !sameWorker() || Read(container+4)!=begin || Read(container+8)!=end)return finish(HintResult::Invalid,"borrowed collection changed");
    if(!out.object || !out.storage)return finish(HintResult::Absent,"native intro/storage absent");
    U32 found=count;
    for(U32 i=0;i<count;++i)if(objects[i]==out.object && out.storage==begin+i*0x270+0x10)found=i;
    if(found==count || !native_subroute_v1::ExactEvaluationRecord(begin,end,found,out.object,Read(begin+found*0x270),out.storage))return finish(HintResult::Invalid,"intro is not exact borrowed record/storage");
    if(Read(out.object)!=At(0x1351E90) || Read(At(0x1351ED4))!=At(0xB596F0))return finish(HintResult::Unsupported,"intro sampler class not supported; no dispatch");
    const U32 readiness=reinterpret_cast<Query>(At(0x4568A0))(reinterpret_cast<void*>(out.worker+0x74));
    if(readiness==0x3C)return finish(HintResult::Busy,"native worker readiness timeout; hint skipped");
    if(readiness)return finish(HintResult::Invalid,"native readiness poll error");
    // Exact +0.0 first argument takes guarded B25D77 -> B27560. No output or
    // object mutation occurs, and no nested provider/curve is touched.
    reinterpret_cast<void(__thiscall*)(void*,const void*,void*)>(At(0xB596F0))(
        reinterpret_cast<void*>(out.object),reinterpret_cast<void*>(At(0x134FB5C)),reinterpret_cast<void*>(out.storage));
    U32 position[4]{};native_subroute_v1::StreamingMatrix matrix{};
    if(!Bytes(out.storage+0x10,position,sizeof(position)) || !native_subroute_v1::BuildStreamingMatrix(position,matrix))return finish(HintResult::Unsupported,"nonfinite/unreadable streaming position; no submission");
    if(!HintScope() || !sameWorker() || Read(container+4)!=begin || Read(container+8)!=end ||
       Read(begin+found*0x270)!=out.object || Read(out.object)!=At(0x1351E90) || !subroute_guards_v1::Signatures(equal,base))return finish(HintResult::Invalid,"pre-submit identity changed");
    // Only while exact worker remains nonnull: EAX is the native poll/signal
    // result. Original caller ignores it; diagnostic records it without waiting.
    out.nativeCode=reinterpret_cast<U32(__thiscall*)(void*,const void*)>(At(0xD4CCA0))(reinterpret_cast<void*>(plugin),&matrix);
    if(out.nativeCode==0x3C)return finish(HintResult::Busy,"native second readiness poll timed out");
    if(out.nativeCode || !sameWorker() || !equal(out.worker+0x80,&matrix,sizeof(matrix)))return finish(HintResult::Invalid,"native submit/error/readback mismatch");
    return finish(HintResult::Submitted,"matrix copied and work event signalled; consumption unclaimed");
}
bool OptionalStreamingHintAllowsFinalization() {
    hintReport=OptionalStreamingHint();
    char text[420]{};
    sprintf_s(text,"STREAM_HINT result=%u reason=%s worker=%08x object=%08x storage=%08x native=%u consumption=unclaimed\r\n",
        unsigned(hintReport.result),hintReport.reason,hintReport.worker,hintReport.object,hintReport.storage,hintReport.nativeCode);
    Log(text);
    // Unsupported is a deliberately unsubmitted OPTIONAL hint. Actual loader,
    // plugin Run, event and registration gates remain in the unchanged finalizer.
    return hintReport.result!=HintResult::Invalid;
}
