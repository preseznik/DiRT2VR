#pragma once
#include <d3d11.h>
#include <cstdint>
namespace vr {
void TraceLightingStage(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye,bool after=false);
void TraceShadowInputs(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye,bool requested=false);
}
