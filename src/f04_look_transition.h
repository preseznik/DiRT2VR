#pragma once
#include <cmath>
#include <cstdint>
class F04LookTransition {
    bool ready=false;
    int x=0,y=0;
    uint64_t until=0;
    static int Normalize(int v) {return std::abs(v)<=8689?0:v;}
public:
    void Observe(int rx,int ry,uint64_t now) {
        rx=Normalize(rx);ry=Normalize(ry);
        if(!ready){ready=true;x=rx;y=ry;if(rx || ry)until=now+500;return;}
        if(std::abs(rx-x)>1024 || std::abs(ry-y)>1024 || (rx==0)!=(x==0) || (ry==0)!=(y==0)) {
            x=rx;y=ry;until=now+500;
        }
    }
    bool Active(uint64_t now) const {return now<until;}
};