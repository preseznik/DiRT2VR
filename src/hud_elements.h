#pragma once
#include <string_view>
#include <charconv>
#include <array>

namespace vr {
struct HudRegion { unsigned bit; float left,top,right,bottom; };
// Standard race HUD layout. These operate only on the transparent cockpit
// overlay, never menus or world geometry. Other UI in a hidden area is hidden too.
inline constexpr std::array<HudRegion,5> HudRegions{{
    {1, .70f,.55f,1.f,1.f}, // gauges
    {2, 0.f,0.f,.36f,.25f}, // lap/time
    {4, .66f,0.f,1.f,.25f}, // position
    {8, .36f,0.f,.66f,.25f}, // map
    {16,0.f,.25f,.13f,1.f} // progress
}};
inline unsigned ParseHudHidden(std::string_view value) {
    unsigned mask{};
    auto result=std::from_chars(value.data(),value.data()+value.size(),mask);
    return result.ec==std::errc{} && result.ptr==value.data()+value.size() && mask<=31 ? mask : 0;
}
}
