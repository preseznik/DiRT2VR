#pragma once
struct IDXGISwapChain;
namespace vr {
// DLSS 5 Neural Rendering while driving (desktop, opt-in). The finished frame,
// HUD included, is processed at Present through the shared helper; each frame
// shows the previous processed one (one frame of latency, even pacing).
bool LiveNeuralRequested();
// Each desktop Present, before Photo Mode (which has its own path).
void LiveNeuralPresent(IDXGISwapChain* swapchain);
}
