#pragma once
#include <dxgi.h>
#include <string>
#include <vector>

namespace vr {
// One setting in the desktop Photo panel.
struct PhotoMenuRow {
    std::wstring section;        // Group heading drawn above this row (empty: same group).
    std::wstring label,value,key; // key: direct keyboard shortcut, shown small (may be empty).
    bool dim{};                  // Greyed: no effect right now (e.g. focus while depth of field is off).
    bool warn{};                 // Value in orange (e.g. unavailable).
};
// What the desktop Photo Mode overlay shows this frame.
struct PhotoPanelState {
    bool panel{};      // Settings panel and hint bar; otherwise only messages.
    bool controller{}; // Hint bar for the Xbox controller instead of keyboard and mouse.
    std::vector<PhotoMenuRow> rows;
    int selected{};
    std::wstring title=L"PHOTO MODE"; // Panel heading.
    std::wstring hints;               // Hint bar text; empty: the Photo Mode controls.
};

// Copies the finished frame now and writes it as a PNG in the background
// (Pictures\DiRT2VR). Call before drawing the panel so the photo stays clean.
void PhotoCapture(IDXGISwapChain* swapchain);
// Draws the panel and/or the latest save message onto the frame.
void PhotoPanelDraw(IDXGISwapChain* swapchain,const PhotoPanelState& state);
// Pure file-name helper, shared with the tests.
std::wstring PhotoFileName(int year,int month,int day,int hour,int minute,int second,int millisecond);
}
