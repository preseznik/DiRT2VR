#pragma once
#include <cstdint>
#include <functional>
struct ID3D11DeviceContext;
struct IDXGISwapChain;
namespace vr {
// DLSS 5 Neural Rendering while driving (desktop, opt-in). The game's HUD draws are
// captured into their own transparent texture, the finished scene is processed at
// Present through the shared helper (showing the previous processed frame), and
// the HUD is composited back on top, so it stays sharp.
bool LiveNeuralRequested();
// From the desktop CreateVertexShader hook: remembers the EGO HUD shaders.
void LiveNeuralVertexShader(void* shader,uint64_t hash);
// From the desktop draw hooks. True when the draw was a HUD draw that went to the
// HUD texture instead (the caller then skips the original draw).
bool LiveNeuralHudDraw(ID3D11DeviceContext* context,const std::function<void()>& draw);
// Each desktop Present, before Photo Mode (which has its own path).
void LiveNeuralPresent(IDXGISwapChain* swapchain);
}
