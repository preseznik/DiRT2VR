#pragma once
#include <dxgi.h>
struct ID3D11Texture2D;
struct ID3D11Device;
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
// VR: both eyes' finished pictures saved side by side (left eye on the left), name ending -SBS.png.
void PhotoCaptureStereo(ID3D11Texture2D* left,ID3D11Texture2D* right);
// Draws the panel and/or the latest save message onto the frame.
void PhotoPanelDraw(IDXGISwapChain* swapchain,const PhotoPanelState& state);
// VR: the Photo panel for the headset layer, set each frame while Photo Mode is open (active=false hides it).
void PhotoHeadsetState(const PhotoPanelState& state,bool active);
// True while a message (photo saved, car followed) is shown.
bool PhotoMessageShown();
// VR: the headset Photo Mode layer (panel=true) or only the current message, drawn into a
// transparent image of this size with premultiplied alpha; null when there is nothing to show.
ID3D11Texture2D* PhotoHeadsetImage(ID3D11Device* device,unsigned width,unsigned height,bool panel);
// VR: the current message on top of the game's captured HUD image (render target, premultiplied alpha).
void PhotoMessageOver(ID3D11Texture2D* hud);
// A short message above the hint bar, drawn by the next PhotoPanelDraw calls for a few seconds.
void PhotoShowMessage(const std::wstring& text,bool error=false);
// Pure file-name helper, shared with the tests.
std::wstring PhotoFileName(int year,int month,int day,int hour,int minute,int second,int millisecond);
}
