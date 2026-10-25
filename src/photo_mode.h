#pragma once
#include <cstdint>
namespace vr {
bool PhotoModeRequested();
bool EnablePhotoMode();
bool PhotoModeActive();
bool PhotoModeBlocksInput();
void PhotoSelectCamera(void* manager);
// The shared native menu check calls this after its original update.
bool PhotoModeMenu(void* state,void* result);
void PhotoModeFrame();
void PhotoModePauseChanged(bool paused);
// Queue window-thread cancellation; native camera restoration stays on an update thread.
void PhotoModeFocusLost();
// Return an isolated record before the engine builds visibility, LOD and lighting.
float* PhotoModeExport(void* owner,float* native);
bool MatchingPhotoCamera(const float* a,const float* b);
}
