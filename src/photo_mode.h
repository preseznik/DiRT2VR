#pragma once
#include <cstdint>
struct IDXGISwapChain;
namespace vr {
bool PhotoModeRequested();
bool EnablePhotoMode();
bool PhotoModeActive();
bool PhotoModeBlocksInput();
// Photo Mode in a replay: the replay's HUD (controls, REPLAY, logo) is not drawn.
bool PhotoModeHidesHud();
// VR: P or A was pressed in Photo Mode; the renderer saves the next stereo pair (true once).
bool PhotoTakeStereoRequest();
void PhotoSelectCamera(void* manager);
// The shared native menu check calls this after its original update.
bool PhotoModeMenu(void* state,void* result);
// Present-time update; on desktop it may save a photo and draw the Photo panel.
void PhotoModeFrame(IDXGISwapChain* swapchain);
void PhotoModePauseChanged(bool paused);
// Queue window-thread cancellation; native camera restoration stays on an update thread.
void PhotoModeFocusLost();
// Return an isolated record before the engine builds visibility, LOD and lighting.
float* PhotoModeExport(void* owner,float* native);
bool MatchingPhotoCamera(const float* a,const float* b);
}
