#include "distance_blur.h"
#include "common.h"
#include "photo_dof.h"
#include "photo_mode.h"
#include <windows.h>

namespace vr {
namespace {
int Level() {
    static const int level=[] {
        wchar_t value[8]{};
        const int chosen=GetEnvironmentVariableW(L"DIRT2VR_DISTANCE_BLUR",value,8)==1 ? value[0]-L'0' : 0;
        const int clamped=chosen>=1 && chosen<=3 ? chosen : 0;
        if(clamped)Log("distance blur: level=%d",clamped);
        return clamped;
    }();
    return level;
}
}

bool DistanceBlurRequested(){return Level()>0;}

void DistanceBlurApply(IDXGISwapChain* swapchain) {
    if(!DistanceBlurRequested() || !swapchain || PhotoModeActive())return;
    // Slight, medium, strong: sharp to 350 / 250 / 180 m, full blur from 2.4 / 1.8 / 1.4 km, at most 2.5 / 5 / 7.5 px
    // at 1080p. The game's own atmosphere already greys these distances; the blur only takes the edge off them.
    static constexpr float start[]{0,350,250,180},full[]{0,2400,1800,1400};
    PhotoDofSettings settings;
    settings.autoFocus=false;settings.blur=Level();
    settings.farStart=start[Level()];settings.farFull=full[Level()];
    static unsigned long long frames{},applied{};
    if(PhotoDofApply(swapchain,settings))++applied;
    if(++frames%600==0)Log("distance blur: frames=%llu applied=%llu",frames,applied);
    // Ask for the next frame's depth (Photo Mode sets this itself while it is open).
    PhotoDofWanted(true);
}
}
