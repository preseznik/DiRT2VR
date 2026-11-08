#pragma once
#include <cstdint>
namespace finalization_v2 {
enum class Phase : unsigned { Dormant, AwaitTrackPulse, PlacementBlocked, Queued, AwaitRegistration, RegisteredBlocked, Failed };
enum class Gate : unsigned { Pending, Ready, Reject };
struct Completion {
    bool owners{}, queueAccepted{}, signalSeen{}, pluginsReady{};
    unsigned entered{}, returned{}, fault{}, unrelated{};
    uint32_t submittingThread{}, runningThread{};
};
inline Gate Finalized(const Completion& s) {
    if(!s.owners || !s.queueAccepted || s.fault || s.unrelated || s.entered>1 || s.returned>1 ||
       (s.returned && s.entered!=1) || (s.runningThread && s.runningThread==s.submittingThread))return Gate::Reject;
    return s.entered==1 && s.returned==1 && s.signalSeen && s.pluginsReady ? Gate::Ready : Gate::Pending;
}
inline Gate Registration(bool requested,bool owners,unsigned update,unsigned render) {
    if(!requested || !owners || update>1 || render>1)return Gate::Reject;
    return update==1 && render==1?Gate::Ready:Gate::Pending;
}
inline bool Expired(uint64_t now,uint64_t then) {return now<then || now-then>=120000;}
}
