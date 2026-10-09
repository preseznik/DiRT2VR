#pragma once
#include <array>
#include <cstddef>
namespace vr {
// Keep the display overlay through rendering, then undo exactly our last output
// before the next native playback update. Never replace a value changed elsewhere.
template<std::size_t N> struct ReplayPoseOverlay {
    std::array<unsigned char,N> original{},displayed{};
    bool committed{};
    void Capture(const std::array<unsigned char,N>& value) {original=value;committed=false;}
    void Commit(const std::array<unsigned char,N>& value) {displayed=value;committed=true;}
    bool Restore(std::array<unsigned char,N>& value) {
        const bool owned=committed && value==displayed;
        committed=false;
        if(owned)value=original;
        return owned;
    }
};
}
