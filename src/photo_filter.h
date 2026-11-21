#pragma once
#include <array>
#include <string>
#include <vector>

struct IDXGISwapChain;
namespace vr {
// One look: colour, contrast and lens effects applied to the finished frame.
struct PhotoLook {
    const wchar_t* name;
    std::array<float,3> tint; // Linear-light channel gains.
    float saturation,contrast,splitTone,sepia,fade,vignette,grain;
};
inline constexpr std::array<PhotoLook,9> PhotoLooks{{
    {L"Off",{1,1,1},1,1,0,0,0,0,0},
    {L"Vivid",{1,1,1},1.35f,1.12f,0,0,0,0,0},
    {L"Warm",{1.08f,1,.88f},1.05f,1.03f,0,0,0,.1f,0},
    {L"Cool",{.9f,1,1.1f},.95f,1.03f,0,0,0,.1f,0},
    {L"Black & white",{1,1,1},0,1.1f,0,0,0,.15f,0},
    {L"Sepia",{1,1,1},1,1.05f,0,1,.15f,.3f,.2f},
    {L"Film",{1.04f,1,.94f},.85f,.95f,0,0,.5f,.35f,.5f},
    {L"Noir",{1,1,1},0,1.35f,0,0,0,.6f,.4f},
    {L"Cinematic",{1,1,1},1.05f,1.1f,1,0,.1f,.25f,.15f},
}};
inline constexpr std::array<float,3> PhotoLetterboxes{0,2.39f,1.85f}; // Off, then target aspect ratios.
// A look made from one of the player's DiRT2VR filter presets (Graphics > Filters):
// only its colour adjustments, approximated on the finished frame.
struct PhotoUserLook {
    std::wstring name;
    float brightness{},contrast{1},saturation{1},gamma{1};
    std::array<float,3> tint{1,1,1}; float tintAmount{};
};
// Loaded once from the launcher's list (DIRT2VR_PHOTO_LOOKS); empty without one.
const std::vector<PhotoUserLook>& PhotoUserLooks();
// Built-in looks first, then the player's.
int PhotoLookCount();
std::wstring PhotoLookName(int look);
struct PhotoFilterSettings {
    int look{};       // Index into PhotoLooks, then PhotoUserLooks.
    float exposure{}; // Stops, -2..2.
    int letterbox{};  // Index into PhotoLetterboxes.
    bool grid{};      // Rule-of-thirds guide: on screen only, never in photos.
};
// Picture height fraction hidden by each letterbox bar; 0 when the frame is already that wide.
inline float PhotoLetterboxBar(float frameAspect,float target) {
    return target>frameAspect && frameAspect>0 ? .5f*(1-frameAspect/target) : 0;
}
// Applies the look, brightness and letterbox to the finished frame (before photos are saved).
bool PhotoFilterApply(IDXGISwapChain* swapchain,const PhotoFilterSettings& settings,unsigned frame);
// Draws the thirds grid inside the visible picture (after photos are saved).
bool PhotoGridDraw(IDXGISwapChain* swapchain,const PhotoFilterSettings& settings);
}
