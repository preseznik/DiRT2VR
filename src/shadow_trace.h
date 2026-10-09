#pragma once
#include <d3d11.h>
#include <cstdint>
namespace vr {
bool ShadowSequenceFrame(uint64_t frame);
void TraceShadowCamera(uint64_t frame,unsigned eye,const float* projection,const float* view);
void TraceShadowRays(uint64_t frame,unsigned eye,uint64_t shader,const float* rays);
void TraceLightingStage(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye,bool after=false);
void TraceShadowInputs(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye,bool requested=false);
}
