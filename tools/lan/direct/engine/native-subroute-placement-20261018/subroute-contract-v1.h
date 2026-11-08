#pragma once
#include <cstdint>
#include <cstddef>
#include <cstring>

// Offline contract only. No game entrypoint, memory access, dispatch, or patching.
namespace native_subroute_v1 {
using U32 = std::uint32_t;
constexpr U32 kRecordStride = 0x270;
constexpr U32 kDiagnosticRecordLimit = 128;
constexpr U32 kDerivedSamplerTable = 0x1351CE0;
constexpr U32 kSamplerThunk = 0xB53E80;
constexpr U32 kSamplerBody = 0xB10FD0;
constexpr U32 kObservedSamplerTable = 0x1351E90;
constexpr U32 kObservedSamplerThunk = 0xB596F0;
struct RouteOwner;
struct Route;
struct AnimationContainer;
struct AnimationObject;
struct EvaluationStorage;
struct StreamingPlugin;
struct SampleArguments16 { U32 words[4]; };
struct alignas(16) StreamingMatrix { U32 words[16]; };
static_assert(sizeof(SampleArguments16)==16);
static_assert(sizeof(StreamingMatrix)==64 && alignof(StreamingMatrix)==16);

#if defined(_MSC_VER) && defined(_M_IX86)
// 00D18D60: borrowed route pointer, RET4; exact owner collection has two slots.
using FindRouteFn = Route* (__thiscall*)(RouteOwner*, const char*);
// 006CBF30: cdecl, caller removes 8 bytes. May return no match.
using FindIntroFn = AnimationObject* (__cdecl*)(AnimationContainer*, EvaluationStorage**);
// Only the audited 00B53E80 -> 00B10FD0 implementation has this return contract.
// EAX = arg2 EvaluationStorage*, RET8. Do not dispatch an arbitrary vtable+44.
using SampleIntroFn = EvaluationStorage* (__thiscall*)(AnimationObject*, const SampleArguments16*, EvaluationStorage*);
// Actual observed table 1351E90: exact zero arguments branch directly to RET8.
// EAX has no output-pointer contract; nested curve/provider branches do not run.
using SampleZeroNoopFn = void (__thiscall*)(AnimationObject*, const SampleArguments16*, EvaluationStorage*);
// 00D4CCA0/00D4C6F0: RET4, no success return. This is a streaming view update.
using SubmitStreamingViewFn = void (__thiscall*)(StreamingPlugin*, const StreamingMatrix*);
// Stronger conditional machine-code contract: ONLY after validating a nonnull,
// retained worker, EAX propagates poll/signal error (0 or timeout60/error). The
// null-worker forwarding branch leaves EAX unspecified. Zero is not consumption.
using SubmitBoundWorkerFn = U32 (__thiscall*)(StreamingPlugin*, const StreamingMatrix*);
#endif

// Proves integer layout, not lifetime or mapping readability. Call only with a
// separately captured, bounded native snapshot. A null empty range is valid.
inline bool RecordCount(U32 begin,U32 end,U32& count) {
    count=0;
    if(!begin || !end) return begin==0 && end==0;
    if((begin&15)!=0 || end<begin) return false;
    const U32 bytes=end-begin;
    if(bytes%kRecordStride || bytes/kRecordStride>kDiagnosticRecordLimit)return false;
    count=bytes/kRecordStride;
    return true;
}
inline bool ExactEvaluationRecord(U32 begin,U32 end,U32 index,U32 returnedObject,
                                  U32 objectAtRecord,U32 returnedStorage) {
    U32 count=0;
    if(!RecordCount(begin,end,count) || index>=count || !returnedObject ||
       returnedObject!=objectAtRecord)return false;
    const U32 record=begin+index*kRecordStride;
    return returnedStorage==record+0x10 && (returnedStorage&15)==0;
}
inline bool FiniteBits(U32 bits) {return (bits&0x7F800000u)!=0x7F800000u;}
// Reproduces 006E04B5..006E055A for XYZ bit-for-bit. Original W is ignored and
// replaced by +1. The additional finite-XYZ refusal is a diagnostic safety gate.
inline bool BuildStreamingMatrix(const U32 position[4],StreamingMatrix& output) {
    if(!position || !FiniteBits(position[0]) || !FiniteBits(position[1]) || !FiniteBits(position[2]))return false;
    StreamingMatrix next{};
    next.words[0]=next.words[5]=next.words[10]=next.words[15]=0x3F800000u;
    std::memcpy(next.words+12,position,3*sizeof(U32));
    output=next;
    return true;
}
// This is only a classification of the known nested branch. It deliberately
// rejects curve/provider variants pending their actual concrete table audit.
inline bool UsesEmbeddedVectorsOnly(U32 link70,U32 link74,U32 curveF8,U32 curveFC) {
    return !link70 && !link74 && !curveF8 && !curveFC;
}
}
