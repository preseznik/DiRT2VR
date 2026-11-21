#pragma once
#include <dxgi.h>
#include <string>

namespace vr {
// What the desktop Photo Mode panel shows this frame.
struct PhotoPanelState {
    bool panel{};      // Full panel with controls; otherwise only a recent save message.
    bool controller{}; // Show Xbox controller help instead of keyboard/mouse help.
    float viewDegrees{},tiltDegrees{};
    int speed{};       // -1 slower, 0 normal, 1 faster.
    int dof{};         // Depth of field: 0 off, 1 on, 2 on but no scene depth available.
    bool autoFocus{};
    float focus{};     // Metres; negative when not yet known.
    int blur{};
    const wchar_t* look=L"Off";
    float exposure{};  // Stops.
    float letterbox{}; // Aspect ratio; 0 off.
    bool grid{};
    bool neuralConfigured{}; // Launcher set a DLSS 5 runtime folder.
    int neural{};            // 0 off, 1 on, 2 starting, 3 failed.
};
// Copies the finished frame now and writes it as a PNG in the background
// (Pictures\DiRT2VR). Call before drawing the panel so the photo stays clean.
void PhotoCapture(IDXGISwapChain* swapchain);
// Draws the panel and/or the latest save message onto the frame.
void PhotoPanelDraw(IDXGISwapChain* swapchain,const PhotoPanelState& state);
// Pure file-name helper, shared with the tests.
std::wstring PhotoFileName(int year,int month,int day,int hour,int minute,int second,int millisecond);
}
