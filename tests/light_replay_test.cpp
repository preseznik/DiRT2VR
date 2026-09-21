#include "light_replay.h"
#include <cstdio>
#include <cstdlib>
#include <thread>

void Check(bool ok,const char* message) {
    if(!ok) { std::fprintf(stderr,"%s\n",message); std::exit(1); }
}
int main() {
    vr::PreparedLights lights;
    int mainContext{},reflectionContext{},headlight{};
    std::vector<vr::LightCall> result;
    Check(lights.Snapshot(10,&mainContext,result) && result.empty(),"day scene without lights");
    std::thread producer([&] {
        lights.Remember(10,{2,nullptr,&headlight,&mainContext,nullptr});
        lights.Remember(10,{1,nullptr,nullptr,&reflectionContext,nullptr});
    });
    producer.join();
    Check(lights.Snapshot(10,&mainContext,result) && result.size()==1 && result[0].light==&headlight,
        "worker-prepared lights must be isolated to the exact main context");
    Check(lights.Snapshot(10,&mainContext,result) && result.size()==1,"second eye must retain the same set");
    Check(lights.Snapshot(11,&mainContext,result) && result.empty(),"do not use pointers from a previous frame");
    lights.Remember(11,{0,nullptr,nullptr,&mainContext,nullptr});
    lights.Remember(10,{2,nullptr,&headlight,&mainContext,nullptr});
    Check(lights.Snapshot(11,&mainContext,result) && result.size()==1 && result[0].kind==0,
        "late old work must not replace the current frame");
    Check(lights.Snapshot(10,&mainContext,result) && result.empty(),"do not use future frame pointers");
    for(size_t i=0;i<vr::PreparedLights::Capacity;++i)
        lights.Remember(12,{0,nullptr,nullptr,&mainContext,nullptr});
    Check(lights.Snapshot(12,&mainContext,result) && result.size()==vr::PreparedLights::Capacity,"capacity boundary");
    lights.Remember(12,{0,nullptr,nullptr,&mainContext,nullptr});
    Check(!lights.Snapshot(12,&mainContext,result) && result.empty(),"overflow must reject the whole set");
    lights.Remember(13,{2,nullptr,&headlight,&mainContext,nullptr});
    Check(lights.Snapshot(13,&mainContext,result) && result.size()==1,"recover on the next prepared frame");
}
