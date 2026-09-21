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
        puts("Eye pose, recenter and asymmetric projection tests passed."); return 0;
    } catch(const std::exception& e) { std::fprintf(stderr,"%s\n",e.what()); return 1; }
}
