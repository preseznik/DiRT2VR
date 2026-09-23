#pragma once
#include <d3d11.h>
#include <cstdint>

namespace vr {
// Called only for an explicitly requested diagnostic frame; never normal play.
void TraceWaterInputs(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye);
}
