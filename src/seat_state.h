#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace vr::seat {
struct Position {
    float x{}, y{}, z{}; // metres: right, up, back
    bool operator==(const Position&) const = default;
};
inline bool Valid(Position p) {
    return std::isfinite(p.x)&&std::isfinite(p.y)&&std::isfinite(p.z)&&
        std::abs(p.x)<=.5f&&std::abs(p.y)<=.5f&&std::abs(p.z)<=.5f;
}
// bits: left, right, up, down, back, forward; opposite directions cancel.
class Movement {
    unsigned previous_{};
    double held_{};
public:
    void Reset() { previous_=0; held_=0; }
    bool Update(Position& p,unsigned directions,double seconds) {
        seconds=std::clamp(seconds,0.0,.1);
        const bool edge=directions!=previous_;
        const double before=held_;
        held_=edge ? 0 : held_+seconds;
        previous_=directions;
        const float step=edge ? .005f : float(std::max(0.0,held_-.35)-std::max(0.0,before-.35))*.05f;
        const auto old=p;
        p.x=std::clamp(p.x+step*(int((directions>>1)&1)-int(directions&1)),-.5f,.5f);
        p.y=std::clamp(p.y+step*(int((directions>>2)&1)-int((directions>>3)&1)),-.5f,.5f);
        p.z=std::clamp(p.z+step*(int((directions>>4)&1)-int((directions>>5)&1)),-.5f,.5f);
        return p!=old;
    }
};
}
