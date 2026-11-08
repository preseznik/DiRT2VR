#pragma once
#include <cstdint>
namespace bootstrap_hold {
using U32=std::uint32_t;
struct Objects { U32 owner{},frontend{},context{},flow{}; };
enum class Gate { Ready, Root, Owner, Frontend, Flow, Context, Started, Changed };
template<class Read> Gate Inspect(Read read,U32 base,U32 flow,Objects& out) {
    out={};const auto at=[base](U32 va){return base+va-0x400000;};
    const auto table=[&](U32 p,U32 vt){return p>=0x10000 && p<0x7fff0000 && read(p)==at(vt);};
    const U32 marker=read(at(0x1482D80));
    if(!table(marker,0x132006C) || !table(marker-0xB98,0x1321CD0)) return Gate::Root;
    const U32 owner=read(marker-0xC);
    if(!table(owner,0x1329A48) || read(owner+0x2CEC)!=marker-0xB98) return Gate::Owner;
    const U32 frontend=read(owner+0x2250);
    if(!table(frontend,0x1300B10)) return Gate::Frontend;
    if(!table(flow,0x12F2A5C) || read(frontend+0x44)!=flow) return Gate::Flow;
    const U32 context=read(frontend+8),a=read(at(0x147DD34)),b=read(at(0x147DCFC));
    if(context<0x10000 || context>=0x7fff0000 || a>=512 || b>=512 ||
       read(flow+0x3C)!=context || read(context+4*a)!=flow || read(context+4*b)!=flow) return Gate::Context;
    if(read(flow+0x18)&0xff000000) return Gate::Started;
    if(read(at(0x1482D80))!=marker || read(marker-0xC)!=owner || read(frontend+0x44)!=flow) return Gate::Changed;
    out={owner,frontend,context,flow};return Gate::Ready;
}
}
