#pragma once
#include <d3d11.h>
#include <cstdint>
namespace vr {
bool PipelineTraceEnabled();
void TracePipelineDraw(ID3D11DeviceContext* context,uint64_t frame,unsigned eye,bool reflection,const char* kind,
    UINT count,UINT instances,uint64_t vertex,uint64_t pixel,bool patched=false);
}
