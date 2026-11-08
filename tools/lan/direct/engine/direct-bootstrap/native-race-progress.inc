// Post-green observations on the held frontend thread. Read only from the
// two-player fixture already bound during setup. -1 means unavailable.
namespace direct_race_progress {
std::atomic<U32> verifiedDrivingSession{};
struct Report {
    int present{-1}, completed{-1}, current{-1}, lapMs[3]{-1,-1,-1};
};

int Protected(U32 address,U32 key,int minimum,int maximum) {
    U32 raw{};
    if(!LoadedAdapterRead(address,raw))return -1;
    const int value=static_cast<int>(raw^key);
    return value>=minimum && value<=maximum ? value : -1;
}

// These small codes identify observed native states, not finishing positions.
int State(U32 root,U32 session,bool player) {
    const U32 component=native_components::Find(Read,root,At(0x1490690));
    if(Read(component)!=At(0x1335C88))return -1;
    const U32 state=Read(component+0x64);
    if(Read(state+0x58)!=session || (player && Read(state+0x68)!=root))return -1;
    const U32 table=Read(state);
    if(table<At(0x12F0000) || table>=At(0x1400000))return -1;
    const U32 getter=Read(table);
    if(getter<base || getter>=At(0x12F0000) || !initial_load_v2::Flag(getter,0xB8) ||
       !initial_load_v2::Flag(getter+5,0xC3))return -1;
    const U32 descriptor=Read(getter+1);
    if(descriptor<At(0x1490000) || descriptor>=At(0x1492000) || Read(component+0x64)!=state)return -1;
    constexpr U32 sessionStates[]{0x1490BD0,0x1490B74,0x1490B90,0x1490BA8,0x1490BC4,0x1490BBC,0x1490BB0};
    constexpr U32 playerStates[]{0x1490BA0,0x1490B68,0x1490AE8,0x1490C24};
    const U32* states=player ? playerStates : sessionStates;
    const unsigned count=player ? _countof(playerStates) : _countof(sessionStates);
    for(unsigned i=0;i<count;++i)if(descriptor==At(states[i]))return static_cast<int>(i+1);
    return 0;
}

bool BoundRoster(const initial_load_v2::Ledger& l) {
    if(l.game!=heldOwner || Read(l.game+0x2CF0)!=l.network || Read(l.network+0x2174)!=l.container ||
       Read(l.container+0x2450)!=l.holder || Read(l.holder)!=At(0x133549C) || Read(l.holder+0x18)!=l.session ||
       Read(l.session)!=At(0x1335844) || Read(l.session+0x1EC)!=l.data || Read(l.data)!=At(0x1336248) ||
       Read(l.data+0x1024)!=l.tree || Read(l.tree)!=At(0x1335B88) || Read(l.data+0x1028)!=committedPlayer ||
       Read(l.roster)!=At(0x13360B4) || Read(l.roster+0x58)!=l.session || Read(l.roster+0x64)!=2 ||
       Read(l.data+0x1068)!=2 || native_components::Find(Read,l.tree,At(0x1490868))!=l.roster)return false;
    const U32 array=Read(l.data+0x1060),ids=Read(l.roster+0x5C);
    U32 roots[2]{},nativeIds[2]{};
    if(!initial_load_v2::Bytes(array,roots,sizeof(roots)) || !initial_load_v2::Bytes(ids,nativeIds,sizeof(nativeIds)) ||
       roots[0]==roots[1] || nativeIds[0]==nativeIds[1] || (roots[0]!=committedPlayer && roots[1]!=committedPlayer))return false;
    for(const auto& p:l.players) {
        U32 identityId{};
        if((roots[0]!=p.root && roots[1]!=p.root) || (nativeIds[0]!=p.nativeId && nativeIds[1]!=p.nativeId) ||
           Read(p.record+0xC)!=p.root || Read(p.record+0x10)!=p.identity || Read(p.identity)!=At(0x1334B8C) ||
           !LoadedAdapterRead(p.identity+4,identityId) || (identityId&255)!=p.nativeId)return false;
    }
    return true;
}

int ConfigLaps(const initial_load_v2::Ledger& l) {
    if(l.config!=initialConfig || l.service!=initialConfigService || Read(l.game+0x2E88)!=l.manager ||
       Read(l.manager)!=At(0x132B1CC) || Read(l.manager+0x34)!=l.service || Read(l.manager+0xAC0)!=1 ||
       Read(l.service)!=At(0x132AC70) || Read(l.service+0x38)!=l.database || Read(l.config)!=At(0x132A450) ||
       Read(l.config+0x3C)!=l.database || Read(l.config+0x40)!=l.configKind || Read(l.config+0x78)!=l.trackRow)return -1;
    const U32 entries=Read(l.config+0x20);
    if(!initial_load_v2::Ptr(entries,8) || Read(l.config+0x24)!=entries+8 ||
       Read(entries)!=l.entries[0] || Read(entries+4)!=l.entries[1])return -1;
    U32 laps{};
    return LoadedAdapterRead(l.config+0x44,laps) && laps<=254 ? static_cast<int>(laps) : -1;
}

U32 RaceClock(const initial_load_v2::Ledger& l) {
    const U32 outer=Read(l.game+0x223C),clock=outer+0xF8;
    if(outer!=l.copies[6] || Read(outer)!=At(0x136092C) || Read(clock)!=At(0x1360454) ||
       Read(clock+4)!=At(0x1360400) || Read(At(0x1360458))!=At(0xCF8480) || Read(clock+0x60)!=2)return 0;
    return clock;
}

struct Crossing {
    int stopped{-1},mode{-1},laps[2]{-1,-1},listed[2]{-1,-1};
};

// The clock keeps its own two physical participants and a list of finishers.
// These can differ from the lap reports sent to the lobby. Index 1 is local.
Crossing ClockCrossing(const initial_load_v2::Ledger& l,U32 clock,U32 key) {
    Crossing clockOnly;
    if(!clock || Read(At(0x1360470)+0x14)!=At(0xCF3D70))return clockOnly;
    U32 mode{},count{},boolKey{};
    unsigned char stopped{};
    if(!LoadedAdapterRead(clock+0x6170,mode) || mode>3 || !LoadedAdapterRead(clock+0x6128,count) || count>2 ||
       !(Read(At(0x147E2C8))&1) || !LoadedAdapterRead(At(0x147E2C0),boolKey) ||
       !initial_load_v2::Byte(clock+0x11,stopped) || stopped>1)return clockOnly;
    clockOnly.stopped=stopped^(boolKey&1);
    clockOnly.mode=static_cast<int>(mode);
    native_vehicle_physics_v1::Snapshot physical{};
    if(native_vehicle_physics_v1::Inspect(LoadedAdapterRead,base,l.game,l.copies[2],l.entries,l.liveries,physical)
       !=native_vehicle_physics_v1::Gate::Ready)return clockOnly;
    Crossing result=clockOnly;
    U32 finishers[2]{},rows[2]{};
    if(count && !initial_load_v2::Bytes(clock+0x612C,finishers,count*sizeof(U32)))return result;
    for(unsigned i=0;i<2;++i) {
        const U32 row=clock+0x68+0xC18*i,car=Read(row+0x84);
        if(Read(row)!=At(0x1360470) || (car!=physical.car[0] && car!=physical.car[1]))return clockOnly;
        U32 role{};
        if(!LoadedAdapterRead(car+0x5588,role) || role>1)return clockOnly;
        const unsigned local=role==0 ? 1 : 0;
        if(rows[local])return clockOnly;
        rows[local]=row;
        unsigned char packed{};
        U32 raw{};
        if(!initial_load_v2::Byte(row+0xC10,packed))return clockOnly;
        if(packed) {
            unsigned short value{};
            if(!initial_load_v2::Bytes(row+0xBBE,&value,sizeof(value)))return clockOnly;
            result.laps[local]=value&0x3FF;
        } else {
            if(!LoadedAdapterRead(row+0x18,raw))return clockOnly;
            result.laps[local]=(raw^key)&0xFFFF;
        }
        result.listed[local]=0;
    }
    for(unsigned i=0;i<count;++i) {
        if(finishers[i]!=rows[0] && finishers[i]!=rows[1])return clockOnly;
        int& listed=result.listed[finishers[i]==rows[1] ? 1 : 0];
        if(listed)return clockOnly;
        listed=1;
    }
    U32 currentCount{};
    if(!LoadedAdapterRead(clock+0x6128,currentCount) || currentCount!=count ||
       !initial_load_v2::Flag(clock+0x11,stopped))return Crossing{};
    return result;
}

void ResultRecord(const initial_load_v2::Ledger& l,const ConfigPlayer& player,U32 key,
                  int& slot,int& status,int& kind) {
    const U32 facade=Read(l.data+0x1274),map=Read(facade+0x6C),records=Read(map+0x60);
    if(Read(facade)!=At(0x1335A38) || Read(l.data+0x126C)!=map || Read(map)!=At(0x1335DA4) ||
       Read(map+0x58)!=l.data || Read(map+0x68)!=2 || records!=map+0x70 ||
       Read(player.record)!=At(0x13344D8))return;
    U32 pair[2]{};
    if(!initial_load_v2::Bytes(records,pair,sizeof(pair)) || pair[0]==pair[1] ||
       (pair[0]!=player.record && pair[1]!=player.record))return;
    slot=pair[0]==player.record ? 0 : 1;
    status=Protected(player.record+0x70,key,0,255);
    U32 value{};
    if(LoadedAdapterRead(player.record+0x64,value) && value<=255)kind=static_cast<int>(value);
}

Report Progress(U32 root,U32 data,bool haveKey,U32 key) {
    Report values;
    const U32 report=native_components::Find(Read,root,At(0x1490830));
    const U32 allocator=Read(data+0x5C),context=Read(data+0x64);
    if(Read(report)!=At(0x1336350) || !initial_load_v2::Ptr(allocator) || !initial_load_v2::Ptr(context) ||
       Read(report+0x54)!=allocator || Read(report+0x58)!=context ||
       Read(At(0x1336350)+0x10)!=At(0x82ACC0) || Read(At(0x1336350)+0x14)!=At(0x847950))return values;
    unsigned char present{};
    if(!initial_load_v2::Byte(report+0x47C,present) || present>1)return values;
    values.present=present;
    if(!present || !haveKey)return values;
    values.completed=Protected(report+0x5C,key,0,254);
    values.current=Protected(report+0x60,key,-1,254);
    for(int i=0;i<values.completed && i<3;++i)
        values.lapMs[i]=Protected(report+0x84+4*i,key,0,0x7FFFFFFF);
    if(values.completed!=Protected(report+0x5C,key,0,254) || values.current!=Protected(report+0x60,key,-1,254) ||
       !initial_load_v2::Flag(report+0x47C,1))return Report{};
    for(int i=0;i<values.completed && i<3;++i)
        if(values.lapMs[i]!=Protected(report+0x84+4*i,key,0,0x7FFFFFFF))return Report{};
    return values;
}

void Policy(U32 root,int& flag,int& reason) {
    const U32 policy=native_components::Find(Read,root,At(0x14906A8));
    unsigned char observed{};
    if(Read(policy)!=At(0x1336390) || Read(At(0x1336390))!=At(0x8B45E0) ||
       !initial_load_v2::Byte(policy+0x68,observed) || observed>1)return;
    flag=observed;
    U32 raw{};
    if(flag && LoadedAdapterRead(policy+0x6C,raw) && raw<=4)reason=static_cast<int>(raw);
}

void Observe(U32 game) {
    static ULONGLONG lastSample{};
    static char previous[2][320]{};
    static char previousCrossing[2][256]{};
    static U32 previousFinishCount=~0u;
    static int previousStopByte=-1;
    const auto& l=initial_load_v2::ledger;
    if(!activation_live_v1::attempt.naturalGreenObserved || game!=l.game || l.thread!=GetCurrentThreadId())return;
    const ULONGLONG now=GetTickCount64();
    const U32 clock=RaceClock(l);
    U32 finishCount=~0u;
    unsigned char stopByte{};
    const int stop=clock && initial_load_v2::Byte(clock+0x11,stopByte) ? stopByte : -1;
    if(clock)LoadedAdapterRead(clock+0x6128,finishCount);
    // Check the first frontend frame after a clock stop or finish-list change.
    if(now-lastSample<100 && finishCount==previousFinishCount && stop==previousStopByte)return;
    lastSample=now;
    if(!BoundRoster(l))return;
    verifiedDrivingSession.store(l.session,std::memory_order_release);
    previousFinishCount=finishCount;
    previousStopByte=stop;
    U32 key{};
    const bool haveKey=(Read(At(0x147DE98))&1) && LoadedAdapterRead(At(0x147DE90),key);
    const int config=ConfigLaps(l),active=clock && haveKey ? Protected(clock+0x14,key,0,254) : -1;
    const Crossing crossing=haveKey ? ClockCrossing(l,clock,key) : Crossing{};
    const int sessionState=State(l.tree,l.session,false);
    for(const auto& p:l.players) {
        const int local=p.root==committedPlayer ? 1 : 0;
        const int playerState=State(p.root,l.session,true);
        const Report report=Progress(p.root,l.data,haveKey,key);
        int flag=-1,reason=-1;
        Policy(p.root,flag,reason);
        char line[320]{};
        sprintf_s(line,"DIRECT_RACE sample local=%d session_state=%d player_state=%d config_laps=%d active_laps=%d report=%d completed=%d current=%d lap1_ms=%d lap2_ms=%d lap3_ms=%d policy_flag=%d policy_reason=%d\r\n",
            local,sessionState,playerState,config,active,report.present,report.completed,report.current,
            report.lapMs[0],report.lapMs[1],report.lapMs[2],flag,reason);
        if(strcmp(previous[local],line)!=0) {
            strcpy_s(previous[local],line);
            Log(line);
        }
        int slot=-1,status=-1,kind=-1;
        if(haveKey)ResultRecord(l,p,key,slot,status,kind);
        char detail[256]{};
        sprintf_s(detail,"DIRECT_RACE crossing local=%d session_state=%d player_state=%d clock_mode=%d clock_stopped=%d clock_laps=%d clock_listed=%d result_slot=%d result_status=%d result_kind=%d\r\n",
            local,sessionState,playerState,crossing.mode,crossing.stopped,crossing.laps[local],
            crossing.listed[local],slot,status,kind);
        if(strcmp(previousCrossing[local],detail)!=0) {
            strcpy_s(previousCrossing[local],detail);
            Log(detail);
        }
    }
}
} // namespace direct_race_progress
