#pragma once
#include "chase_orbit.h"
#include <array>
namespace vr {
struct ChaseVector { float x{},y{},z{}; };
struct ChaseQuaternion { float x{},y{},z{},w{1}; };
inline ChaseQuaternion ChaseMultiply(ChaseQuaternion a,ChaseQuaternion b) {
    return {a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,
        a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z};
}
inline ChaseVector ChaseRotate(ChaseQuaternion q,ChaseVector v) {
    const auto r=ChaseMultiply(ChaseMultiply(q,{v.x,v.y,v.z,0}),{-q.x,-q.y,-q.z,q.w});return {r.x,r.y,r.z};
}
inline bool OrbitChasePose(std::array<float,8>& pose,ChaseVector pivot,float yaw,float pitch,float distanceScale=1) {
    for(float value:pose)if(!std::isfinite(value))return false;
    if(!std::isfinite(pivot.x)||!std::isfinite(pivot.y)||!std::isfinite(pivot.z)||!std::isfinite(yaw)||!std::isfinite(pitch)||
       !std::isfinite(distanceScale)||distanceScale<.6f||distanceScale>1.8f)return false;
    ChaseQuaternion q{pose[0],pose[1],pose[2],pose[3]};
    const float norm=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
    if(std::abs(norm-1.f)>.01f)return false;
    ChaseVector relative{pose[4]-pivot.x,pose[5]-pivot.y,pose[6]-pivot.z};
    const float radius=relative.x*relative.x+relative.y*relative.y+relative.z*relative.z;
    if(radius<.25f || radius>2500.f)return false;
    if(yaw==0 && pitch==0 && distanceScale==1)return true; // Off/neutral is byte-identical.
    auto right=ChaseRotate(q,{1,0,0});
    const float s=std::sin(pitch*.5f);
    auto rotation=ChaseMultiply({0,std::sin(yaw*.5f),0,std::cos(yaw*.5f)},{right.x*s,right.y*s,right.z*s,std::cos(pitch*.5f)});
    q=ChaseMultiply(rotation,q);relative=ChaseRotate(rotation,relative);
    pose[0]=q.x;pose[1]=q.y;pose[2]=q.z;pose[3]=q.w;
    pose[4]=pivot.x+relative.x*distanceScale;pose[5]=pivot.y+relative.y*distanceScale;pose[6]=pivot.z+relative.z*distanceScale;
    return true;
}
}
