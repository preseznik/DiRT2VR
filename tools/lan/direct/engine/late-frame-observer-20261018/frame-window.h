#pragma once
#include <cstdint>
namespace late_frame {
using U32=std::uint32_t;
struct Owners {U32 game{},render{},collection{},thread{};};
inline bool Same(const Owners&a,const Owners&b){return a.game==b.game&&a.render==b.render&&a.collection==b.collection&&a.thread==b.thread;}
enum class Wait {Update,Render,Phase2};
// A scheduling observation, never permission to publish a new vehicle.
struct Window {
 Owners owners{};bool bound{},primed{},failed{},update{},render{},phase2{},inside{};U32 epoch{},lastPhysics{},windows{};
 bool Bind(Owners o){if(!o.game||!o.render||!o.collection||!o.thread)return false;if(bound&&!Same(owners,o)){failed=true;return false;}owners=o;bound=true;return !failed;}
 void WaitReturned(Owners o,Wait kind,U32 result){if(!Bind(o))return;if(result){failed=true;return;}if(inside){failed=true;return;}if(kind==Wait::Update){if(update||phase2)failed=true;update=true;}else if(kind==Wait::Render){if(render||phase2)failed=true;render=true;}else {if(phase2)failed=true;phase2=true;}}
 bool Enter(Owners o,U32 physics,bool worldIdle){if(inside){failed=true;return false;}if(!Bind(o))return false;inside=true;++epoch;if(!primed){primed=true;lastPhysics=physics;return false;}const bool good=!failed&&update&&render&&phase2&&worldIdle&&physics>lastPhysics;if(good)++windows;lastPhysics=physics;return good;}
 void Leave(){inside=false;update=render=phase2=false;}
};
}
