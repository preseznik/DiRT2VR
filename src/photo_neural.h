#pragma once
struct IDXGISwapChain;
struct ID3D11Texture2D;
namespace vr {
// DLSS 5 Neural Rendering for desktop Photo Mode, through the x64 helper.
enum class PhotoNeuralState { Off, Starting, On, Failed };
// True when the launcher passed a runtime folder (with nvngx_dlssnr.dll) and the helper exists.
bool PhotoNeuralConfigured();
// Processes the finished frame when wanted; starts the helper on first use and
// keeps the original frame while it starts or after it fails.
PhotoNeuralState PhotoNeuralApply(IDXGISwapChain* swapchain,bool wanted);
// Live driving: sends this frame and replaces it with the previous processed frame,
// through the same helper. False while the helper starts or after it fails.
bool PhotoNeuralProcessLive(ID3D11Texture2D* frame);
}
