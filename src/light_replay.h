#pragma once
#include <cstdint>
#include <mutex>
#include <vector>

namespace vr {
struct LightCall { unsigned kind; void* self; void* light; void* context; void* material; };

// Borrowed engine objects live only through the current prepared scene. The
// producer runs on a worker; serial rendering joins it before either eye runs.
class PreparedLights {
    std::mutex mutex_;
    uint64_t frame_{};
    bool started_{},overflow_{};
    std::vector<LightCall> calls_;
public:
    static constexpr size_t Capacity=512;
    void Remember(uint64_t frame,LightCall call) {
        std::lock_guard lock(mutex_);
        if(started_ && frame<frame_) return;
        if(!started_ || frame!=frame_) {
            frame_=frame; started_=true; overflow_=false; calls_.clear();
        }
        if(calls_.size()==Capacity) { overflow_=true; return; }
        calls_.push_back(call);
    }
    bool Snapshot(uint64_t frame,void* context,std::vector<LightCall>& result) {
        std::lock_guard lock(mutex_);
        result.clear();
        if(!started_ || frame!=frame_) return true;
        if(overflow_) return false; // Fall back to screen, never refresh half a set.
        for(const auto& call:calls_) if(call.context==context) result.push_back(call);
        return true;
    }
};
}
