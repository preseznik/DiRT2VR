#pragma once
namespace vr {
bool SteeringAnimationRequested();
bool EnableSteeringAnimation(bool headset=true);
void SteeringSelectCamera(void* manager);
void SteeringCockpitView(bool active);
}
