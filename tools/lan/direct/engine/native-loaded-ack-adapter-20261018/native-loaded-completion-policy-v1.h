#pragma once
#include <cstdint>
namespace loaded_completion_v1 {
// Exact signed comparisons in 6CA460; a pending record phase is not completion.
inline bool VehicleRangeComplete(std::uint32_t first,std::uint32_t last,std::uint32_t serial) {
 const auto a=static_cast<std::int32_t>(first),b=static_cast<std::int32_t>(last),s=static_cast<std::int32_t>(serial);
 return b>=a ? s>=a && s<=b : s>=a || s<=b;
}
template<class Token> bool Successful(const Token& t,unsigned succeeded) {
 return t.submitted && t.value!=0xffffffffu && static_cast<unsigned>(t.state)==succeeded && t.nativeState==3 && t.waitResult==0;
}
// Counters are captured by the actual original-Run observer, never supplied by
// an API caller as permission. A return on the submitting thread is rejected.
inline bool Retired(unsigned observerArmed,unsigned entered,unsigned returned,unsigned fault,unsigned unrelated,
                    std::uint32_t submittingThread,std::uint32_t worker) {
 return observerArmed==1 && entered==1 && returned==1 && !fault && !unrelated && submittingThread && worker && worker!=submittingThread;
}
}
