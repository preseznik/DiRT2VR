#define DIRECTINPUT_VERSION 0x0800
#include <windows.h>
#include <dinput.h>
#include <cassert>
#include <cmath>
#include <cstdio>
#include "axes.h"

struct Driver {
    bool writable{}, readable{true}, invalid{};
    HRESULT GetObjectInfo(DIDEVICEOBJECTINSTANCEW* out,DWORD offset,DWORD how) {
        assert(how==DIPH_BYOFFSET);
        if(offset!=DIJOFS_X && offset!=DIJOFS_RZ) return DIERR_OBJECTNOTFOUND;
        out->dwType=DIDFT_ABSAXIS;
        out->dwOfs=4096+offset; // Driver-native offsets deliberately differ.
        return DI_OK;
    }
    HRESULT SetProperty(REFGUID,LPCDIPROPHEADER p) {
        assert(p->dwHow==DIPH_BYOFFSET);
        return writable?DI_OK:DIERR_UNSUPPORTED;
    }
    HRESULT GetProperty(REFGUID,LPDIPROPHEADER p) {
        if(!readable) return DIERR_UNSUPPORTED;
        auto* range=reinterpret_cast<DIPROPRANGE*>(p);
        range->lMin=invalid?100:(writable?0:-32768);
        range->lMax=invalid?0:(writable?65535:32767);
        return DI_OK;
    }
};
int main() {
    Driver wheel;
    auto axes=ReadAxes(&wheel);
    assert(axes.mask==((1u<<0)|(1u<<5)));
    assert(NormalizeAxis(-32768,axes.ranges[0])==-1.f);
    assert(NormalizeAxis(32767,axes.ranges[0])==1.f);
    assert(std::abs(NormalizeAxis(0,axes.ranges[0]))<0.001f);
    wheel.writable=true; axes=ReadAxes(&wheel);
    assert(axes.mask==33 && axes.ranges[0].maximum==65535);
    wheel.readable=false; axes=ReadAxes(&wheel);
    assert(axes.mask==33); // Known successfully set range is still usable.
    wheel.writable=false; assert(ReadAxes(&wheel).mask==0);
    wheel.readable=true; wheel.invalid=true; assert(ReadAxes(&wheel).mask==0);
    assert(NormalizeAxis(500,{0,1000})==0.f);
    assert(NormalizeAxis(1200,{0,1000})==1.f);
    std::puts("PASS native axis slots, read-only ranges, normalization and failed-property handling");
}
