#pragma once
#include "combined-policy-v1.h"
#include "../native-query-retirement-20261018/query-retirement.h"
#include "../native-physics-retirement-v1-20261018/physics-observer.h"
namespace combined_noop_v1 {
// These fields are copied from the concrete native query observer, never
// supplied as permission. Typed owner/fresh phase checks remain mandatory.
struct QueryProof {U32 epoch{},serial{},thread{},currentPhase{};bool called{},retired{},failed{};query_retirement::Receipt native{};};
inline Gate SameFrame(U32 epoch,U32 serial,U32 appThread,const QueryProof&q,const cloth_observation::Result&c,const physics_observation::Result&p){
 if(!epoch||!serial||!appThread)return Gate::Frame;
 if(q.epoch!=epoch||q.serial!=serial||q.thread!=appThread||q.currentPhase||!q.called||!q.retired||q.failed||!query_retirement::Complete(q.native))return Gate::Query;
 if(!c.observed||!c.original_returned||c.epoch!=epoch||c.serial!=serial||!c.master||c.syncCalls!=1||c.syncReturns!=1||c.syncResult)return Gate::Cloth;
 if(!p.observed||p.epoch!=epoch||!physics_observation::Complete(p.receipt,serial))return Gate::Physics;
 // Physics is owned by its native update thread. It need not equal App.
 return Gate::Observed;
}
}
