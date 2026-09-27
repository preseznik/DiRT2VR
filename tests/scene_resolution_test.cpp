#include "scene_resolution.h"
#include <stdexcept>
#include <cstdio>
#include <cstring>

void Require(bool value,const char* message) { if(!value) throw std::runtime_error(message); }
vr::SceneRenderer Fixture() {
    vr::SceneRenderer r{};
    r.backend=4; r.window=r.swap.OutputWindow=reinterpret_cast<HWND>(1);
    r.swap.Windowed=TRUE;
    r.width=r.windowWidth=r.swap.BufferDesc.Width=1280;
    r.height=r.windowHeight=r.swap.BufferDesc.Height=720;
    r.desktopWidth=r.fullscreenWidth=3840;
    r.desktopHeight=r.fullscreenHeight=2160;
    return r;
}
int main() {
    try {
        for(unsigned scale : {50u,100u,150u,200u,300u}) {
            auto r=Fixture();
            Require(vr::SetSceneResolution(r,16*scale,12*scale),"supported scale rejected");
            Require(r.width==16*scale && r.height==12*scale &&
                r.swap.BufferDesc.Width==r.width && r.swap.BufferDesc.Height==r.height &&
                r.windowWidth==r.width && r.windowHeight==r.height,"engine and swapchain must agree");
            Require(r.desktopWidth==3840 && r.desktopHeight==2160 &&
                r.fullscreenWidth==3840 && r.fullscreenHeight==2160,"desktop dimensions changed");
        }
        for(unsigned fault=0;fault<8;++fault) {
            auto r=Fixture(); unsigned w=2400,h=1800;
            switch(fault) {
            case 0:r.backend=3;break;
            case 1:r.swap.Windowed=FALSE;break;
            case 2:r.swap.OutputWindow=nullptr;break;
            case 3:r.width=1600;break;
            case 4:r.windowHeight=1080;break;
            case 5:w=4801;break;
            case 6:h=0;break;
            case 7:h=3601;break;
            }
            const auto before=r;
            Require(!vr::SetSceneResolution(r,w,h),"incompatible renderer accepted");
            Require(!memcmp(&r,&before,sizeof(r)),"rejected request changed renderer");
        }
        puts("PASS scene dimensions, desktop preservation and incompatible-layout rejection"); return 0;
    } catch(const std::exception& e) { puts(e.what()); return 1; }
}
