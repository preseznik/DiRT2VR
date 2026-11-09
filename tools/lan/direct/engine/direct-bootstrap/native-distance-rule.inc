// Private two-player Rallycross: a stationary disqualification can leave the
// other driver racing alone. Award that driver's win at the set distance.
#include "direct-laps.h"
namespace direct_distance_rule {
using namespace direct_race_progress;
using LastDriverWin=bool(__thiscall*)(void*);
LastDriverWin originalLastDriverWin{};

bool BeforeFinishLine(int target,const Crossing& clock,int local,int driverFlag,int peerFlag,int peerReason) {
    return direct_laps::Valid(target) && clock.mode==1 && clock.stopped==0 && clock.listed[local]==0 &&
        clock.laps[local]>0 && clock.laps[local]<target && driverFlag==0 && peerFlag==1 && peerReason==1;
}

bool __fastcall LastDriverWinAtDistance(void* playerState,void*) {
    const bool award=originalLastDriverWin(playerState);
    if(!award || _ReturnAddress()!=reinterpret_cast<void*>(At(0x8BEF86)))return award;
    const U32 state=reinterpret_cast<U32>(playerState);
    const U32 session=verifiedDrivingSession.load(std::memory_order_acquire);
    if(!session || Read(state+0x58)!=session || Read(state)!=At(0x1337120))return award;
    const auto& l=initial_load_v2::ledger;
    if(l.session!=session || !BoundRoster(l) || ConfigLaps(l)!=RxLaps())return award;
    const U32 root=Read(state+0x68),fsm=native_components::Find(Read,root,At(0x1490690));
    if(Read(fsm)!=At(0x1335C88) || Read(fsm+0x64)!=state ||
       (root!=l.players[0].root && root!=l.players[1].root))return award;

    const U32 clock=RaceClock(l);
    U32 key{};
    if(!clock || !(Read(At(0x147DE98))&1) || !LoadedAdapterRead(At(0x147DE90),key))return award;
    const int target=Protected(clock+0x14,key,0,254);
    if(target!=RxLaps())return award;
    const int local=root==committedPlayer ? 1 : 0;
    const Crossing crossing=ClockCrossing(l,clock,key);
    int driverFlag=-1,driverReason=-1,peerFlag=-1,peerReason=-1;
    Policy(root,driverFlag,driverReason);
    Policy(root==l.players[0].root ? l.players[1].root : l.players[0].root,peerFlag,peerReason);
    if(!BeforeFinishLine(target,crossing,local,driverFlag,peerFlag,peerReason))return award;

    char line[128]{};
    sprintf_s(line,"DIRECT_RACE distance local=%d active_laps=%d clock_laps=%d\r\n",local,target,crossing.laps[local]);
    Log(line);
    return false;
}

bool Install() {
    // The new-lap handler has already recorded the lap before asking whether
    // to award a win by default. The ordinary VehicleFinished path is separate.
    constexpr unsigned char predicate[]{0x83,0xec,0x10,0x8b,0x41,0x58,0x53,0x55,0x56};
    constexpr unsigned char newLapCall[]{0x8b,0xcf,0xe8,0xda,0xb0,0xff,0xff,0x84,0xc0,0x74,0x49};
    return trace::ReadableEqual(reinterpret_cast<void*>(At(0x8BA060)),predicate,sizeof(predicate)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x8BEF7F)),newLapCall,sizeof(newLapCall)) &&
        MH_CreateHook(reinterpret_cast<void*>(At(0x8BA060)),reinterpret_cast<void*>(LastDriverWinAtDistance),
                      reinterpret_cast<void**>(&originalLastDriverWin))==MH_OK;
}
} // namespace direct_distance_rule
