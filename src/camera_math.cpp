#include "camera_math.h"
#include <cmath>
#include <algorithm>
#include <stdexcept>

namespace vr {
namespace {
XrQuaternionf Normalize(XrQuaternionf q) {
    const float length=std::sqrt(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w);
    if(!std::isfinite(length) || length<0.001f) throw std::runtime_error("invalid eye orientation");
    return {q.x/length,q.y/length,q.z/length,q.w/length};
}
XrQuaternionf Multiply(XrQuaternionf a,XrQuaternionf b) {
    return {a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,
        a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z};
}
XrVector3f Rotate(XrQuaternionf q,XrVector3f v) {
    const auto result=Multiply(Multiply(q,{v.x,v.y,v.z,0}),{-q.x,-q.y,-q.z,q.w});
    return {result.x,result.y,result.z};
}
}
XrPosef CenterPose(const std::array<XrView,2>& views) {
    const auto& a=views[0].pose; const auto& b=views[1].pose;
    const auto q=Normalize(a.orientation),r=Normalize(b.orientation);
    const float sign=q.x*r.x+q.y*r.y+q.z*r.z+q.w*r.w<0 ? -1.f : 1.f;
    return {Normalize({q.x+sign*r.x,q.y+sign*r.y,q.z+sign*r.z,q.w+sign*r.w}),
        {(a.position.x+b.position.x)*.5f,(a.position.y+b.position.y)*.5f,(a.position.z+b.position.z)*.5f}};
}
XrPosef RelativePose(const XrPosef& reference,const XrPosef& eye) {
    const auto q=Normalize(reference.orientation);
    const XrQuaternionf inverse{-q.x,-q.y,-q.z,q.w};
    return {Normalize(Multiply(inverse,Normalize(eye.orientation))),
        Rotate(inverse,{eye.position.x-reference.position.x,eye.position.y-reference.position.y,eye.position.z-reference.position.z})};
}
XrPosef ScreenPose(const XrPosef& reference,float distance) {
    const auto q=Normalize(reference.orientation);
    const auto offset=Rotate(q,{0,0,-distance});
    return {q,{reference.position.x+offset.x,reference.position.y+offset.y,reference.position.z+offset.z}};
}
bool CockpitCameraCandidate(const float* a,const float* b) {
    // Verified cockpit XML/live records use 0.075; trailer cameras use 0.2,
    // bumper cameras 0.1 and the sampled replay cameras approximately 0.05.
    return std::abs(a[21]-.075f)<.00001f && std::abs(b[21]-.075f)<.00001f;
}
void ApplyEyePose(float* camera,const XrPosef& pose,float unitsPerMetre) {
    if(!std::isfinite(unitsPerMetre) || unitsPerMetre<=0 ||
       !std::isfinite(pose.position.x) || !std::isfinite(pose.position.y) || !std::isfinite(pose.position.z))
        throw std::runtime_error("invalid eye translation or world scale");
    const auto q=Normalize(pose.orientation);
    const std::array<float,3> right{-camera[8],-camera[9],-camera[10]},up{camera[4],camera[5],camera[6]},back{-camera[12],-camera[13],-camera[14]};
    const auto x=Rotate(q,{1,0,0}),y=Rotate(q,{0,1,0}),z=Rotate(q,{0,0,1});
    for(unsigned i=0;i<3;++i) {
        const auto world=[&](XrVector3f v) { return right[i]*v.x+up[i]*v.y+back[i]*v.z; };
        camera[4+i]=world(y); camera[8+i]=-world(x); camera[12+i]=-world(z);
        camera[16+i]+=unitsPerMetre*world(pose.position);
    }
}
void ApplyFov(float* projection,const XrFovf& fov) {
    for(float angle:{fov.angleLeft,fov.angleRight,fov.angleDown,fov.angleUp})
        if(!std::isfinite(angle) || std::abs(angle)>1.55f) throw std::runtime_error("invalid eye FOV");
    const float l=std::tan(fov.angleLeft),r=std::tan(fov.angleRight),d=std::tan(fov.angleDown),u=std::tan(fov.angleUp);
    if(r-l<0.01f || u-d<0.01f || projection[11]!=-1.f || projection[15]!=0.f)
        throw std::runtime_error("unsupported eye projection");
    projection[0]=2/(r-l); projection[5]=2/(u-d);
    projection[8]=(r+l)/(r-l); projection[9]=(u+d)/(u-d);
}
void MultiplyMatrices(const float* a,const float* b,float* result) {
    std::array<float,16> product{};
    for(unsigned r=0;r<4;++r) for(unsigned c=0;c<4;++c)
        for(unsigned k=0;k<4;++k) product[4*r+c]+=a[4*r+k]*b[4*k+c];
    for(unsigned i=0;i<16;++i) result[i]=product[i];
}
bool VisibilityBox(const float* a,const float* b,float* matrix) {
    for(const float* camera:{a,b}) {
        for(unsigned i:{16u,17u,18u,22u}) if(!std::isfinite(camera[i])) return false;
        if(camera[22]<25 || camera[22]>5000) return false;
    }
    // Include either original camera and head/eye translation margin. Keep the
    // engine's distance/LOD logic; this replaces only the directional frustum.
    const float range=std::max(a[22],b[22])+8.f;
    std::array<float,16> result{};
    for(unsigned axis=0;axis<3;++axis) {
        const float center=(a[16+axis]+b[16+axis])*.5f;
        const float extent=range+std::abs(a[16+axis]-b[16+axis])*.5f;
        const float scale=(axis==2 ? -1.f : 1.f)/extent;
        result[axis*5]=scale; result[12+axis]=-center*scale;
    }
    result[15]=1;
    for(unsigned i=0;i<16;++i) matrix[i]=result[i];
    return true;
}
}
