#pragma once
#include <d3d11.h>
#include <cstddef>

namespace vr {
// Verified DX11 renderer layout for the supported 1.1 executable. Change the
// engine's dimensions before it allocates its scene targets, not just DXGI's copy.
struct SceneRenderer {
    unsigned char prefix[0x18];
    unsigned backend;
    unsigned unused1;
    unsigned width, height;
    unsigned char unused2[0x48];
    HWND window;
    unsigned char unused3[0x58];
    DXGI_SWAP_CHAIN_DESC swap;
    unsigned desktopWidth, desktopHeight;
    unsigned fullscreenWidth, fullscreenHeight;
    unsigned windowWidth, windowHeight;
};
static_assert(offsetof(SceneRenderer, window)==0x70);
static_assert(offsetof(SceneRenderer, swap)==0xcc);
static_assert(offsetof(SceneRenderer, windowWidth)==0x118);

inline bool SetSceneResolution(SceneRenderer& renderer, unsigned width, unsigned height) {
    if(width<560 || width>4800 || height<420 || height>3600 ||
       renderer.backend!=4 || !renderer.swap.Windowed ||
       renderer.window!=renderer.swap.OutputWindow ||
       renderer.width!=renderer.swap.BufferDesc.Width || renderer.height!=renderer.swap.BufferDesc.Height ||
       renderer.windowWidth!=renderer.width || renderer.windowHeight!=renderer.height) return false;
    renderer.width=renderer.windowWidth=renderer.swap.BufferDesc.Width=width;
    renderer.height=renderer.windowHeight=renderer.swap.BufferDesc.Height=height;
    return true;
}
}
