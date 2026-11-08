#pragma once
#include <cstdint>
namespace initial_load_v2 {
enum class Phase : unsigned { Dormant, Preload, Vehicles, Track, FinalizationBlocked, Failed, ResourceCheckpoint, CommonAudio };
enum class JobState : unsigned { Unsubmitted, Pending, Succeeded, Failed, CompletionUnknown };
struct Token {
    uint32_t value{}, nativeState{0xffffffffu}, waitResult{0xffffffffu};
    JobState state{JobState::Unsubmitted};
    bool submitted{};
};
// WAIT_OBJECT_0 alone is insufficient: unknown tokens and failed jobs also signal.
inline JobState Classify(bool found, uint32_t nativeState, uint32_t waitResult) {
    if (!found) return JobState::CompletionUnknown;
    if (nativeState == 4) return JobState::Failed;
    if (waitResult != 0 && waitResult != 0x3c) return JobState::Failed;
    if (nativeState == 3) return waitResult == 0 ? JobState::Succeeded : JobState::Pending;
    if (nativeState == 0 || nativeState == 1 || nativeState == 2) return JobState::Pending;
    return JobState::CompletionUnknown; // State0 is original queued-before-start, not completion.
}
inline void Observe(Token& token, bool found, uint32_t state, uint32_t wait) {
    if (!token.submitted || token.state == JobState::Succeeded || token.state == JobState::Failed || token.state == JobState::CompletionUnknown) return;
    token.nativeState=state; token.waitResult=wait; token.state=Classify(found,state,wait);
}
inline bool Failed(const Token& token) { return token.state==JobState::Failed || token.state==JobState::CompletionUnknown; }
inline bool Expired(uint64_t now,uint64_t began) { return now < began || now-began >= 120000; }
struct Filters { bool checkpointExclude, dominationExclude, checkpointInclude; };
inline Filters ForKind(uint32_t kind) { return {kind!=3,kind!=5,kind==3}; }
inline unsigned FilterCount(Filters f) { return unsigned(f.checkpointExclude)+unsigned(f.dominationExclude); }
inline bool LocalLoadingState(uint32_t table,uint32_t getter,uint32_t descriptor,uint32_t flags) {
    if(flags!=0x0101)return false; // Native local authority + active bytes.
    return (table==0x1337060 && getter==0x8C58D0 && descriptor==0x1490BD4) ||
           (table==0x1337150 && getter==0x8C5920 && descriptor==0x1490BB4);
}
}
