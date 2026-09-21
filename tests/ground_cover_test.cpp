#include "ground_cover.h"
#include <cstdlib>
void Check(bool condition) { if(!condition) std::abort(); }
int main() {
    vr::GroundCoverPair pair; vr::GroundCoverKey key;
    key.streams[1]={123,48,0}; key.indices=1758;
    unsigned count=18,first=0;
    Check(!pair.Apply(1,key,count,first)); // No active pair.
    pair.Begin(); pair.Apply(1,key,count,first);
    count=13; first=18; pair.Apply(1,key,count,first);
    count=first=0; Check(pair.Apply(2,key,count,first) && count==18 && first==0);
    count=first=0; Check(pair.Apply(2,key,count,first) && count==13 && first==18);
    count=first=0; Check(!pair.Apply(2,key,count,first)); // No extra batch reuse.
    pair.End(); Check(!pair.Apply(2,key,count,first));
    pair.Begin(); Check(!pair.Apply(2,key,count,first)); // Previous frame unavailable.
    pair.Begin(); count=18; pair.Apply(1,key,count,first);
    auto changed=key; changed.streams[1].buffer=456;
    count=0; Check(!pair.Apply(2,changed,count,first) && count==0);
    Check(!pair.Apply(2,key,count,first)); // Mismatch invalidates matching order.
    pair.Begin(); count=18; pair.Apply(1,key,count,first);
    count=7; Check(!pair.Apply(2,key,count,first) && count==7);
    pair.Begin(); count=18; for(unsigned i=0;i<65;++i) pair.Apply(1,key,count,first);
    count=0; Check(!pair.Apply(2,key,count,first)); // Capacity overflow fails closed.
}
