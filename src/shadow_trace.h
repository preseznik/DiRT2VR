#pragma once
#include <d3d11.h>
#include <cstdint>
namespace vr {
void TraceShadowInputs(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye);
}
