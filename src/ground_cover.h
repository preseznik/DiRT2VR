#pragma once
#include <array>
#include <cstdint>

namespace vr {
// Match only the same ordered ground-cover draws inside one stereo pair.
// The second render clears instance counts/offsets, but retains the mesh and
// instance streams. No simulation or GPU buffer contents are replayed here.
struct GroundCoverKey {
    struct Stream { uintptr_t buffer{}; unsigned stride{},offset{}; bool operator==(const Stream&) const = default; };
    std::array<Stream,32> streams{};
    uintptr_t index{},layout{};
    unsigned format{},indexOffset{},topology{},indices{},start{};
    int base{};
    bool operator==(const GroundCoverKey&) const = default;
};
class GroundCoverPair {
    struct Batch { GroundCoverKey key; unsigned instances{},first{}; };
    std::array<Batch,64> batches_{};
    unsigned count_{},cursor_{};
    bool active_{},valid_{};
public:
    void Begin() { count_=cursor_=0; active_=valid_=true; }
    void End() { active_=valid_=false; count_=cursor_=0; }
    bool Apply(unsigned eye,const GroundCoverKey& key,unsigned& instances,unsigned& first) {
        if(!active_ || !valid_) return false;
        if(eye==1) {
            if(count_==batches_.size() || instances>1000000 || first>1000000-instances) { valid_=false; return false; }
            batches_[count_++]={key,instances,first}; return false;
        }
        if(eye!=2) return false;
        if(cursor_==count_) { valid_=false; return false; }
        const auto& batch=batches_[cursor_++];
        if(!(batch.key==key)) { valid_=false; return false; }
        // Only repair the observed empty-repeat case, never a changed nonempty draw.
        if(instances!=0 || first!=0 || batch.instances==0) return false;
        instances=batch.instances; first=batch.first; return true;
    }
};
}
