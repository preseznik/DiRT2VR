#pragma once
#include <cmath>
namespace vr {
inline bool ValidDesktopShadowDistance(unsigned distance) {
    return distance==0 || distance==160 || distance==240 || distance==320;
}
// Never shorten an authored native range or alter the near cascade.
inline bool ExtendOuterShadowRange(float nearRange,float& farRange,unsigned distance) {
    if(!distance || !ValidDesktopShadowDistance(distance) ||
       !std::isfinite(nearRange) || !std::isfinite(farRange) ||
       nearRange<=0 || nearRange>=farRange || farRange>5000 ||
       farRange>=float(distance)) return false;
    farRange=float(distance);return true;
}
void EnableDesktopShadowDistance();
}
