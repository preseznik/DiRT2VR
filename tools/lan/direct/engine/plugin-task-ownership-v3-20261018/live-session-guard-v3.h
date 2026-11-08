#pragma once
#include <cstdint>
// Read-only current owner links. Cached retired graphs are not authority.
namespace finalization_live_v3 {
using U32=std::uint32_t;
struct SessionBinding {
    U32 game{},network{},container{},holder{},session{},transport{},data{},tree{},local{},localId{};
    bool host{};
};
enum class Gate { Ready, Pointer, CurrentNetwork, CurrentContainer, CurrentHolder,
                  Holder, Transport, Session, Provider, LocalIdentity, Registration };
inline bool Ptr(U32 p,U32 size) {return p>=0x10000 && size && std::uint64_t(p)+size<=0x80000000ull;}
template<class Read> U32 Byte(Read read,U32 p) {return (read(p&~U32(3))>>(8*(p&3)))&255;}
template<class Read> Gate Inspect(Read read,U32 base,const SessionBinding& b,bool registrationSubmitted) {
    const auto at=[base](U32 va){return base+va-0x400000;};
    if(!Ptr(b.game,0x2E82) || !Ptr(b.network,0x2178) || !Ptr(b.container,0x2454) ||
       !Ptr(b.holder,0x804) || !Ptr(b.session,0x1F0) || !Ptr(b.transport,12) ||
       !Ptr(b.data,0x102C) || !Ptr(b.tree,4) || !Ptr(b.local,4) || b.localId>255)return Gate::Pointer;
    if(read(b.game+0x2CF0)!=b.network)return Gate::CurrentNetwork;
    if(read(b.network+0x2174)!=b.container || read(b.container)!=at(0x13364EC))return Gate::CurrentContainer;
    if(read(b.container+0x2450)!=b.holder)return Gate::CurrentHolder;
    const U32 lifecycle=b.host?1u:2u;
    if(read(b.holder)!=at(0x133549C) || read(b.holder+0x18)!=b.session ||
       read(b.holder+0x24)!=b.transport || read(b.holder+8)!=lifecycle || Byte(read,b.holder+0x800)!=0)return Gate::Holder;
    if(read(b.transport)!=at(0x135CFC0) || read(b.transport+8)!=lifecycle)return Gate::Transport;
    if(read(b.session)!=at(0x1335844) || read(b.session+0x24)!=(b.host?0u:1u) ||
       Byte(read,b.session+0x6C)!=1 || read(b.session+0x1EC)!=b.data)return Gate::Session;
    if(read(b.data)!=at(0x1336248) || read(b.data+0x64)!=read(b.network+0x1004) ||
       read(b.data+0x68)!=b.network+0x1010 || read(b.data+0x1024)!=b.tree ||
       read(b.tree)!=at(0x1335B88))return Gate::Provider;
    if(read(b.data+0x1028)!=b.local || Byte(read,b.session+0x28)!=b.localId)return Gate::LocalIdentity;
    const U32 update=Byte(read,b.game+0x2E80),render=Byte(read,b.game+0x2E81);
    if(update>1 || render>1 || (!registrationSubmitted && (update || render)))return Gate::Registration;
    // Recheck the current path after inspecting the retained children.
    if(read(b.game+0x2CF0)!=b.network)return Gate::CurrentNetwork;
    if(read(b.network+0x2174)!=b.container)return Gate::CurrentContainer;
    if(read(b.container+0x2450)!=b.holder)return Gate::CurrentHolder;
    if(read(b.holder+0x18)!=b.session || read(b.holder+0x24)!=b.transport ||
       read(b.holder+8)!=lifecycle || Byte(read,b.holder+0x800)!=0)return Gate::Holder;
    if(read(b.session+0x1EC)!=b.data || Byte(read,b.session+0x6C)!=1)return Gate::Session;
    return Gate::Ready;
}
}
