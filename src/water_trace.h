#pragma once
#include <d3d11.h>
#include <cstdint>

namespace vr {
// Bounded CPU-side observations for ordinary logging. These never read back GPU
// contents or retain references to game resources. Flush after the stereo pair.
bool InitializeWaterSurfaceTrace();
bool TraceWaterShaderCreationEnabled(uint64_t shader);
void ObserveWaterShaderCreation(ID3D11PixelShader* shader,uint64_t hash,uint64_t frame,double milliseconds);
void ObserveWaterShaderBinding(ID3D11DeviceContext* context,ID3D11PixelShader* shader);
void TraceWaterSurfaceDraw(ID3D11DeviceContext* context,uint64_t frame,unsigned eye,const char* kind,
    unsigned count,unsigned instances,unsigned start,int base,unsigned firstInstance);
void FlushWaterSurfaceTrace(uint64_t frame);
// Called only for an explicitly requested diagnostic frame; never normal play.
void TraceWaterInputs(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye);
}
