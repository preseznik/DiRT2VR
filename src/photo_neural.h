#pragma once
#include "desktop_bridge_protocol.h"
#include "photo_overlay.h"
#include <cstdint>
#include <utility>
#include <vector>
struct IDXGISwapChain;
struct ID3D11Texture2D;
namespace vr {
// DLSS 5 Neural Rendering for desktop Photo Mode, through the x64 helper.
enum class PhotoNeuralState { Off, Starting, On, Failed };
// True when the launcher passed a runtime folder (with nvngx_dlssnr.dll) and the helper exists.
bool PhotoNeuralConfigured();
// Processes the finished frame when wanted; starts the helper on first use and
// keeps the original frame while it starts or after it fails. While sceneKey (the
// camera) and the settings stay the same, the last result is shown again: DLSS 5
// amplifies the game's tiny frame-to-frame differences, so a still would flicker.
PhotoNeuralState PhotoNeuralApply(IDXGISwapChain* swapchain,bool wanted,uint64_t sceneKey);
// Live driving: sends this frame and replaces it with the previous processed frame,
// through the same helper. False while the helper starts or after it fails.
bool PhotoNeuralProcessLive(ID3D11Texture2D* frame);
// The look used for both Photo Mode and driving: launcher defaults
// (DIRT2VR_NEURAL_STYLE/STRENGTH/TONE/DETAIL/PASSES/MIX/RESOLUTION/FOVEATION), changed in-game for the session.
bridge::NeuralSettings PhotoNeuralSettings();
void PhotoNeuralSetSettings(const bridge::NeuralSettings& settings);
// The option rows shared by the Photo panel and the driving panel (each adds its own
// on/off row first); dimmed while neural rendering is off.
enum class NeuralOption { Style,Strength,Tone,Detail,Passes,Mix,Resolution,Foveation };
std::vector<std::pair<NeuralOption,PhotoMenuRow>> NeuralOptionRows(bool enabled);
// Left/right on one of those rows.
void NeuralOptionAdjust(NeuralOption option,int direction);
}
