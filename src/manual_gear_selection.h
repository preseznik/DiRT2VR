#pragma once

namespace vr {
// Native gated input uses 9 for neutral and -1 for reverse; zero means that no
// H-pattern request was made. A parked shifter must not undo a paddle shift.
class ManualGearSelection {
    int previousGate{9};
    bool initialized{}, gated{};
public:
    void Reset() { initialized=false;gated=false;previousGate=9; }
    int Select(int gate, bool sequentialShift) {
        if(!initialized) {
            initialized=true;gated=gate!=9;
        } else if(gate!=previousGate) {
            gated=true;
        }
        previousGate=gate;
        if(sequentialShift)gated=false;
        return gated?gate:0;
    }
};
}
