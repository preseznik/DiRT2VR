#pragma once
#include <array>
#include <cmath>
#include <cstring>
namespace vr {
// The supported renderer stores near/far at +0x40/+0x44. Its cockpit pass
// changes near to 0.05; each eye must start with the original scene range.
class ScopedSceneDepth {
    void* target_{};
    std::array<float,2> original_{};
public:
    explicit ScopedSceneDepth(void* target,bool enabled=true) {
        if(!target || !enabled)return;
        std::memcpy(original_.data(),target,sizeof(original_));
        if(std::isfinite(original_[0]) && std::isfinite(original_[1]) &&
           original_[0]>0 && original_[0]<original_[1])target_=target;
    }
    ~ScopedSceneDepth(){if(target_)std::memcpy(target_,original_.data(),sizeof(original_));}
    ScopedSceneDepth(const ScopedSceneDepth&)=delete;
    ScopedSceneDepth& operator=(const ScopedSceneDepth&)=delete;
};
}
