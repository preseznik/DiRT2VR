#include "hud_elements.h"
#include <cstdlib>
#include <cstdio>
#define CHECK(x) do { if(!(x)) { std::fprintf(stderr,"Failed line %d: %s\n",__LINE__,#x); std::exit(1); } } while(0)
int main() {
    unsigned bits=0;
    for(const auto& r:vr::HudRegions) {
        CHECK(!(bits&r.bit)); bits|=r.bit;
        CHECK(r.left>=0 && r.top>=0 && r.right<=1 && r.bottom<=1 && r.left<r.right && r.top<r.bottom);
        CHECK(!(r.left<.5f && r.right>.5f && r.top<.5f && r.bottom>.5f));
    }
    CHECK(bits==31);
    for(auto text:{"","-1","32","1x","0x10","999999999999999"}) CHECK(vr::ParseHudHidden(text)==0);
    CHECK(vr::ParseHudHidden("31")==31 && vr::ParseHudHidden("21")==21 && vr::ParseHudHidden("0")==0);
    std::puts("HUD region masks are independent, bounded and preserve the central view; invalid selections rejected.");
}
