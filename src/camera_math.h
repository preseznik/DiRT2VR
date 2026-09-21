#pragma once
#include <openxr/openxr.h>
#include <array>

namespace vr {
XrPosef CenterPose(const std::array<XrView,2>& views);
XrPosef RelativePose(const XrPosef& reference,const XrPosef& eye);
// Only the verified XYZ basis/position fields in the 0x70-byte record change.
void ApplyEyePose(float* camera,const XrPosef& pose,float unitsPerMetre);
// Preserve this engine's depth convention; replace its angular projection.
void ApplyFov(float* projection,const XrFovf& fov);
void MultiplyMatrices(const float* a,const float* b,float* result);
}
