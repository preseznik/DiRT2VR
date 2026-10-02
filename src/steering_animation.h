#pragma once
namespace vr {
bool SteeringAnimationRequested();
bool EnableSteeringAnimation(bool headset=true);
void SteeringSelectCamera(void* manager);
void SteeringCockpitView(bool active);
// Preserve the last confirmed view while a new main frame is being prepared.
// A fallback/exception clears it; a completed frame publishes its real state.
class SteeringCockpitFrame {
    bool completed_{};
public:
    SteeringCockpitFrame()=default;
    SteeringCockpitFrame(const SteeringCockpitFrame&)=delete;
    SteeringCockpitFrame& operator=(const SteeringCockpitFrame&)=delete;
    ~SteeringCockpitFrame() { if(!completed_)SteeringCockpitView(false); }
    void Complete(bool active) { SteeringCockpitView(active);completed_=true; }
};
}
