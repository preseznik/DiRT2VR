#pragma once
#include <string_view>
namespace vr {
enum class ChaseMouseMode { Right, Left, Always };
inline ChaseMouseMode ParseChaseMouseMode(std::string_view value) {
    return value=="left"?ChaseMouseMode::Left:value=="always"?ChaseMouseMode::Always:ChaseMouseMode::Right;
}
struct ChaseMouseState { bool active{},held{},neutral{}; };
inline ChaseMouseState ChaseMouseActivation(ChaseMouseMode mode,bool left,bool right,bool moved,bool available) {
    const bool always=mode==ChaseMouseMode::Always;
    const bool button=mode==ChaseMouseMode::Left?left:right;
    const bool active=available && (always || button);
    // Always-on still permits automatic return once mouse movement stops.
    // Wait for a neutral sample after focus/pause changes, including the click
    // used to return to the game window.
    return {active,active && (always?moved:button),always?!left && !right && !moved:!button};
}
}
