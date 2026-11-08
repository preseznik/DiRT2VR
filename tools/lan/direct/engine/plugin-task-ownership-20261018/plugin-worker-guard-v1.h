#pragma once
#include <cstdint>
#include <cstring>

// Read-only inspection of the pinned Win32 engine's initialized scheduler.
// This is an identity/active-state gate, not a native lifetime reference or lock.
namespace plugin_worker_v1 {
using U32=std::uint32_t;
constexpr U32 Pending=258,Signalled=0; // Win32 zero-time wait results.
struct Snapshot {
    U32 manager{},allocator{},task{},managerTaskHead{},managerTaskNode{},managerTaskCount{};
    U32 taskHead{},taskNode{},worker{},workerHead{},workerTaskNode{},workerTaskCount{};
    U32 managerWorkerHead{},managerWorkerNode{},managerWorkerCount{};
    U32 started{},stopped{},finished{},taskEvent{},waitCount{},waitTaskIndex{};
};
enum class Gate { Ready, Manager, TaskMap, TaskWorkers, Worker, WorkerMap, Reciprocal,
                  Events, WaitList, Starting, Stopping, Changed };
inline bool Pointer(U32 p){return p>=0x10000 && p<=0x7ffffe00 && (p&3)==0;}
template<class Read> U32 Byte(Read read,U32 p){return (read(p&~U32(3))>>(8*(p&3)))&255;}

// Both native sets/maps use the usual left,parent,right tree links. Sets have
// one pointer payload and nil byte+11; maps have two and nil byte+15.
template<class Read> bool Member(Read read,U32 header,U32 count,U32 valueOffset,U32 nilOffset,
                                U32 wanted,U32 limit,U32& matched) {
    matched=0;
    if(!Pointer(header) || !count || count>limit || limit>128 || Byte(read,header+nilOffset)!=1)return false;
    struct PendingNode {U32 p,parent;};
    PendingNode pending[128]{};U32 seen[128]{};unsigned queued=1,total=0;
    pending[0]={read(header+4),header};
    while(queued) {
        const auto node=pending[--queued];
        if(node.p==header)continue;
        if(!Pointer(node.p) || total>=count || Byte(read,node.p+nilOffset)!=0 || read(node.p+4)!=node.parent)return false;
        for(unsigned i=0;i<total;++i)if(seen[i]==node.p)return false;
        seen[total++]=node.p;
        if(read(node.p+valueOffset)==wanted){if(matched)return false;matched=node.p;}
        if(queued+2>128)return false;
        pending[queued++]={read(node.p),node.p};pending[queued++]={read(node.p+8),node.p};
    }
    if(total!=count || !matched)return false;
    bool left=false,right=false;
    for(unsigned i=0;i<total;++i){left|=seen[i]==read(header);right|=seen[i]==read(header+8);}
    return left && right;
}
template<class Read,class Wait> Gate Inspect(Read read,Wait wait,U32 base,U32 manager,U32 task,Snapshot& out) {
    out={};const auto at=[base](U32 va){return base+va-0x400000;};
    if(!Pointer(manager) || !Pointer(task) || !Pointer(read(manager)) || read(task)!=read(manager))return Gate::Manager;
    Snapshot s{};s.manager=manager;s.allocator=read(manager);s.task=task;
    s.managerTaskHead=read(manager+0x5C);s.managerTaskCount=read(manager+0x60);
    if(!Member(read,s.managerTaskHead,s.managerTaskCount,0x10,0x15,task,128,s.managerTaskNode))return Gate::TaskMap;
    // The four shipped plugin_finalise configurations each assign exactly one
    // worker. A different topology is deliberately outside this first probe.
    s.taskHead=read(task+0x14);s.taskNode=read(s.taskHead+4);
    if(read(task+0x18)!=1 || !Pointer(s.taskHead) || !Pointer(s.taskNode) || s.taskNode==s.taskHead ||
       read(s.taskHead)!=s.taskNode || read(s.taskHead+8)!=s.taskNode || read(s.taskNode)!=s.taskHead ||
       read(s.taskNode+4)!=s.taskHead || read(s.taskNode+8)!=s.taskHead ||
       Byte(read,s.taskHead+0x11)!=1 || Byte(read,s.taskNode+0x11)!=0)return Gate::TaskWorkers;
    s.worker=read(s.taskNode+0xC);
    if(!Pointer(s.worker) || read(s.worker)!=at(0x13CD260) || (read(s.worker+0x144)&255)!=0)return Gate::Worker;
    s.managerWorkerHead=read(manager+0x44);s.managerWorkerCount=read(manager+0x48);
    if(!Member(read,s.managerWorkerHead,s.managerWorkerCount,0x10,0x15,s.worker,64,s.managerWorkerNode))return Gate::WorkerMap;
    s.workerHead=read(s.worker+0x30);s.workerTaskCount=read(s.worker+0x34);
    if(!Member(read,s.workerHead,s.workerTaskCount,0xC,0x11,task,63,s.workerTaskNode))return Gate::Reciprocal;
    const U32 wrappers[]{s.worker+8,s.worker+0x38,s.worker+0x10,task+0xE4};
    U32 handles[4]{};
    for(unsigned i=0;i<4;++i){
        if(read(wrappers[i])!=at(0x13C88A8))return Gate::Events;
        handles[i]=read(wrappers[i]+4);
        if(!handles[i] || handles[i]==0xffffffff)return Gate::Events;
        for(unsigned j=0;j<i;++j)if(handles[j]==handles[i])return Gate::Events;
    }
    s.started=handles[0];s.stopped=handles[1];s.finished=handles[2];s.taskEvent=handles[3];
    s.waitCount=read(s.worker+0x140);s.waitTaskIndex=0xffffffff;
    if(s.waitCount!=s.workerTaskCount+1 || s.waitCount>64 || read(s.worker+0x40)!=s.worker+0x38)return Gate::WaitList;
    for(U32 i=1;i<s.waitCount;++i)if(read(s.worker+0x40+4*i)==task+0xE4){
        if(s.waitTaskIndex!=0xffffffff)return Gate::WaitList;s.waitTaskIndex=i;
    }
    if(s.waitTaskIndex==0xffffffff)return Gate::WaitList;
    // These three events are constructed manual-reset by1196D80/11597A0(0,0).
    // Querying them does not consume the worker's stop or completion signal.
    if((read(s.worker+4)&255)!=1 || wait(s.stopped)!=Pending || wait(s.finished)!=Pending)return Gate::Stopping;
    if(wait(s.started)!=Signalled)return Gate::Starting;
    if(read(task+0x18)!=1 || read(task+0x14)!=s.taskHead || read(manager+0x5C)!=s.managerTaskHead ||
       read(s.worker+0x30)!=s.workerHead || read(s.worker+0x140)!=s.waitCount)return Gate::Changed;
    out=s;return Gate::Ready;
}
inline bool Same(const Snapshot& a,const Snapshot& b){return std::memcmp(&a,&b,sizeof(a))==0;}
}
