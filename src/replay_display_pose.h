#pragma once
#include "scene_camera.h"
#include <array>
namespace vr {
// Copy only the verified XYZ basis and position into the exported camera.
// Preserve its object header, padding, projection and clipping distances.
inline bool CopyReplayDisplayPose(std::array<float,28>& camera,const std::array<float,16>& pose) {
    auto candidate=camera;
    for(unsigned row=0;row<4;++row)
        for(unsigned axis=0;axis<3;++axis)candidate[4+row*4+axis]=pose[row*4+axis];
    if(!MatchingSceneCamera(candidate.data(),candidate.data()))return false;
    camera=candidate;return true;
}
}
