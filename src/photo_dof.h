#pragma once
#include <cmath>

struct ID3D11Device;
struct ID3D11DeviceContext;
struct IDXGISwapChain;
namespace vr {
struct PhotoDofSettings {
    bool autoFocus=true; // Focus on whatever is at the centre of the picture.
    float focus=10;      // Manual focus distance in metres.
    int blur=5;          // 1..10.
};
// Blur radius in pixels at 1080p for a pixel at `distance` metres. Pixels within
// 7.5% of the focus distance, and at least 1.5 m (so a whole car stays sharp),
// stay sharp; blur grows to full strength one focus distance beyond that band.
inline float PhotoDofCircle(float distance,float focus,int blur) {
    if(!(distance>0) || !(focus>0))return 0;
    const float maxRadius=2.5f*float(blur<1?1:blur>10?10:blur);
    const float band=.075f*focus>1.5f?.075f*focus:1.5f;
    const float x=(std::abs(distance-focus)-band)/focus;
    return maxRadius*(x<0?0:x>1?1:x);
}
// The game's hardware depth to view distance, from the shadow-mask pass's screenProj.zw.
// Its GL-style projection is stored directly: depth = -z - w/distance (lab readings
// z=-1.00003, w=0.6: a 0.3 m near plane and about 20 km far plane). Cleared depth is far.
inline float PhotoDofDistance(float depth,float z,float w) {
    const float denominator=-z-depth;
    return depth<1 && denominator>1e-6f?w/denominator:1e6f;
}
// Desktop Photo Mode only: hooks used to copy the scene depth from the game's shadow-mask pass.
void AttachPhotoDof(ID3D11Device* device,ID3D11DeviceContext* context);
// While true, the next shadow-mask pass copies its depth input.
void PhotoDofWanted(bool wanted);
// Blurs the finished frame. False when no scene depth arrived this frame.
bool PhotoDofApply(IDXGISwapChain* swapchain,const PhotoDofSettings& settings);
// Distance at the picture centre in metres, read back from the GPU; negative when unknown.
float PhotoDofCentre();
}
