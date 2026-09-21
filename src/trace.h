#pragma once
#include <d3d11.h>
namespace vr {
void AttachTrace(ID3D11Device* device, ID3D11DeviceContext* context, IDXGISwapChain* swapchain);
}
