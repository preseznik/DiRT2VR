#pragma once
#include <d3d11.h>

// Moving scenery on external tracks: wind-turbine rotors that turn and a train that follows its
// railway. Stock DiRT 2 cannot animate track meshes, so the pack builder writes each moving part as its
// own world-space mesh whose first vertices carry a marker and the part's motion (never indexed, so
// never drawn). When the game creates such a vertex buffer it is registered here; for the draws that
// use it, the object shader's cb0 `model` (offset 0, row vectors) becomes L * model, where L is the
// part's world-space motion since load, and `prevModel` (offset 256, velocity passes) likewise.
//
// Marker, in the float3 positions of the first vertices (stride bytes apart):
//   v0 = (kMarker, stride, kind)
//   kind 1, rotor:      v1 = hub (m), v2 = unit axis, v3 = (rpm, 0, 0)
//   kind 2, train part: v1 = (motion, distance at load cm, wheelbase cm), v2 = part position (cm, x along,
//                       y right, z up), v3 = (radius cm, speed cm/s, 0), v4 = (railway points, 0, 0),
//                       then that many railway points (Unreal cm, closed loop) when this mesh carries them.
//   motion: 0 car body, 1 fixed part, 2 wheel, 3 side rod, 4 bogie.
namespace vr {
constexpr float kSceneryMotionMarker=-987654.25f;
// Device and context hooks for desktop sessions, where the trace hooks are not installed; in headset
// sessions trace.cpp forwards its Map/Unmap/DrawIndexed hooks here instead.
void AttachSceneryMotion(ID3D11Device* device,ID3D11DeviceContext* context,bool ownDrawHooks);
// Observers for the trace hooks (headset sessions).
void SceneryMotionMapped(ID3D11Resource* resource,D3D11_MAP mode,const D3D11_MAPPED_SUBRESOURCE* mapped);
void SceneryMotionUnmapped(ID3D11Resource* resource);
// Draws the indexed call with moved scenery when its vertex buffer is a registered moving part; returns
// false (nothing drawn) otherwise. `draw` issues the original call.
template<class Draw> bool SceneryMotionDraw(ID3D11DeviceContext* context,Draw&& draw);
bool SceneryMotionPrepare(ID3D11DeviceContext* context,ID3D11Buffer*& original,ID3D11Buffer*& moved);
void SceneryMotionRestore(ID3D11DeviceContext* context,ID3D11Buffer* original);
template<class Draw> bool SceneryMotionDraw(ID3D11DeviceContext* context,Draw&& draw) {
    ID3D11Buffer* original{};ID3D11Buffer* moved{};
    if(!SceneryMotionPrepare(context,original,moved))return false;
    draw();
    SceneryMotionRestore(context,original);
    return true;
}
}
