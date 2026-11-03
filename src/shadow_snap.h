#pragma once
#include <cmath>
namespace vr {
// The native fit re-centres each cascade on the camera every frame without
// texel alignment, so shadow edges crawl as the view moves; far more visibly
// once Shadow distance or Detailed distance stretch a map. A 1/256 step is a
// whole number of texels for every map size the game offers (512-2048).
constexpr float ShadowSnapStep=2.f/256.f;

// Row-vector light matrices: light = world * view, clip = world * projection.
inline bool ShadowMatrix(const float* m) {
    for(int i=0;i<16;++i) if(!std::isfinite(m[i])) return false;
    // The engine's own products leave w a few ulps from one.
    return std::abs(m[3])<1e-6f && std::abs(m[7])<1e-6f && std::abs(m[11])<1e-6f && std::abs(m[15]-1.f)<1e-5f;
}

// Moves the light across its own direction by less than one snap step, so
// the cascade's texel grid stays fixed in the world. Depth, extent and the
// fitted region are unchanged.
struct ShadowSnap {
    float light[2];  // added to the light view translation
    float clip[2];   // added to the view-projection translation
    float world[3];  // added to the light position
};

// The projection must be the orthonormal light view scaled per axis, the
// orthographic fit the game builds. offset (lab canary) is added in clip units.
inline bool ShadowSnapShift(const float* view,const float* projection,ShadowSnap& snap,float offset=0.f) {
    if(!ShadowMatrix(view) || !ShadowMatrix(projection)) return false;
    float scale[3]{};
    for(int col=0;col<3;++col) {
        float length=0.f,dot=0.f;
        for(int row=0;row<3;++row) {
            length+=view[row*4+col]*view[row*4+col];
            dot+=view[row*4+col]*projection[row*4+col];
        }
        if(std::abs(length-1.f)>1e-3f) return false;
        scale[col]=dot;
        for(int row=0;row<3;++row)
            if(std::abs(view[row*4+col]*dot-projection[row*4+col])>1e-4f*std::abs(dot)+1e-7f) return false;
    }
    if(std::abs(scale[0])<1e-6f || std::abs(scale[1])<1e-6f) return false;
    for(int axis=0;axis<2;++axis) {
        const float origin=projection[12+axis];
        snap.clip[axis]=std::round(origin/ShadowSnapStep)*ShadowSnapStep-origin+offset;
        snap.light[axis]=snap.clip[axis]/scale[axis];
    }
    for(int j=0;j<3;++j) snap.world[j]=-(snap.light[0]*view[j*4]+snap.light[1]*view[j*4+1]);
    return true;
}
inline void ShiftShadowView(float* view,const ShadowSnap& s) { view[12]+=s.light[0]; view[13]+=s.light[1]; }
inline void ShiftShadowProjection(float* projection,const ShadowSnap& s) { projection[12]+=s.clip[0]; projection[13]+=s.clip[1]; }
inline void ShiftShadowPosition(float* position,const ShadowSnap& s) { for(int j=0;j<3;++j) position[j]+=s.world[j]; }
}
