#include "camera_math.h"
#include <cmath>
#include <cstdio>
#include <stdexcept>

void Check(bool value) { if(!value) throw std::runtime_error("camera math assertion"); }
bool Near(float a,float b) { return std::abs(a-b)<0.00001f; }
int main() {
    try {
        std::array<float,28> camera{};
        camera[5]=1; camera[8]=-1; camera[14]=-1; camera[19]=1; camera[7]=73;
        vr::ApplyEyePose(camera.data(),{{0,0,0,1},{.032f,.1f,-.2f}},2.f);
        Check(Near(camera[16],.064f) && Near(camera[17],.2f) && Near(camera[18],-.4f));
        Check(camera[7]==73 && camera[19]==1);
        const float s=std::sqrt(.5f);
        vr::ApplyEyePose(camera.data(),{{0,s,0,s},{0,0,0}},1);
        Check(Near(camera[12],-1) && Near(camera[14],0));
        auto relative=vr::RelativePose({{0,s,0,s},{5,2,1}},{{0,s,0,s},{5,2,.968f}});
        Check(Near(relative.position.x,.032f) && Near(relative.position.z,0) && Near(relative.orientation.w,1));
        std::array<XrView,2> eyes{};
        eyes[0].pose={{0,0,0,1},{-.032f,1,2}}; eyes[1].pose={{0,0,0,-1},{.032f,1,2}};
        auto center=vr::CenterPose(eyes); Check(Near(center.position.x,0) && Near(center.orientation.w,1));
        std::array<float,16> projection{}; projection[10]=-1.01f; projection[11]=-1; projection[14]=-.1f;
        XrFovf fov{-.8f,.6f,.7f,-.5f}; vr::ApplyFov(projection.data(),fov);
        Check(Near(std::tan(fov.angleLeft)*projection[0]-projection[8],-1));
        Check(Near(std::tan(fov.angleRight)*projection[0]-projection[8],1));
        Check(Near(std::tan(fov.angleDown)*projection[5]-projection[9],-1));
        Check(Near(std::tan(fov.angleUp)*projection[5]-projection[9],1));
        Check(projection[10]==-1.01f && projection[14]==-.1f);
        bool rejected=false; try { vr::ApplyFov(projection.data(),{.8f,-.8f,.7f,-.7f}); } catch(...) { rejected=true; }
        Check(rejected);
        auto screen=vr::ScreenPose({{0,s,0,s},{5,2,1}},2);
        Check(Near(screen.position.x,3) && Near(screen.position.y,2) && Near(screen.position.z,1));
        Check(Near(screen.orientation.y,s) && Near(screen.orientation.w,s));
        XrPosef reference{{0,0,0,1},{0,1,0}},turned{{0,s,0,s},{.2f,1,0}};
        auto fixedHud=vr::ScreenPose(reference,4),followingHud=vr::ScreenPose(turned,4);
        Check(Near(fixedHud.position.z,-4) && Near(fixedHud.position.x,0));
        Check(Near(followingHud.position.x,-3.8f) && Near(followingHud.position.z,0));
        auto seenFixed=vr::RelativePose(turned,fixedHud),seenFollowing=vr::RelativePose(turned,followingHud);
        Check(Near(seenFixed.position.x,4) && Near(seenFixed.position.z,-.2f));
        Check(Near(seenFollowing.position.x,0) && Near(seenFollowing.position.z,-4));
        for(float distance:{1.f,6.5f,20.f}) {
            auto fixed=vr::ScreenPose(reference,distance),follow=vr::ScreenPose(turned,distance);
            Check(Near(fixed.position.z,-distance));
            auto relativeFollow=vr::RelativePose(turned,follow);
            Check(Near(relativeFollow.position.x,0) && Near(relativeFollow.position.z,-distance));
        }
        std::array<float,28> a{},b{}; a[21]=b[21]=.075f;
        Check(vr::CockpitCameraCandidate(a.data(),b.data()));
        for(float nearPlane:{.2f,.1f,.05f}) {
            a[21]=nearPlane; Check(!vr::CockpitCameraCandidate(a.data(),b.data()));
            a[21]=.075f; b[21]=nearPlane; Check(!vr::CockpitCameraCandidate(a.data(),b.data()));
            b[21]=.075f;
        }
        a[16]=b[16]=234; a[17]=b[17]=18; a[18]=b[18]=-456;
        a[22]=b[22]=1000;
        std::array<float,16> box;
        Check(vr::VisibilityBox(a.data(),b.data(),box.data()));
        for(unsigned axis=0;axis<3;++axis) for(float direction:{-1.f,1.f}) {
            float clip=(a[16+axis]+direction*1000)*box[axis*5]+box[12+axis];
            Check(std::abs(clip)<1); // Left/right, above/below, ahead/behind.
            clip=(a[16+axis]+direction*1100)*box[axis*5]+box[12+axis];
            Check(std::abs(clip)>1); // Bounded, rather than an infinite frustum.
        }
        Check(box[0]*box[5]*box[10]<0 && box[15]==1); // Engine clip handedness.
        a[22]=0;
        Check(!vr::VisibilityBox(a.data(),b.data(),box.data()));
        puts("Eye pose, recenter, projection and visibility box tests passed."); return 0;
    } catch(const std::exception& e) { std::fprintf(stderr,"%s\n",e.what()); return 1; }
}
