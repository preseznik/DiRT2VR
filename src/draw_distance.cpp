#include "draw_distance.h"
#include "common.h"
#include "gfwl_compat.h"
#include <array>
#include <intrin.h>

namespace vr {
namespace {
template<class T> T& Field(void* object,size_t offset) {
    return *reinterpret_cast<T*>(static_cast<unsigned char*>(object)+offset);
}
using RouteUpdateFn=void(__thiscall*)(void*,float);
using CarApplyFn=void(__thiscall*)(void*);
using WorldDistanceFn=void(__thiscall*)(void*,float,float);
RouteUpdateFn originalRoute{};
CarApplyFn originalCars{};
using GrassLoadFn=bool(__thiscall*)(void*);
using TreeReadFn=void(__thiscall*)(void*,void*);
GrassLoadFn originalGrass{};
using GrassViewFn=void(__thiscall*)(void*,void*,void*,void*,int,void*);
GrassViewFn originalGrassView{};
using GrassCreateViewFn=void*(__thiscall*)(void*,void*);
GrassCreateViewFn originalGrassCreateView{};
using GrassIndicesFn=void(__thiscall*)(void*);
GrassIndicesFn originalGrassIndices{};
TreeReadFn originalTreeAttributes{},originalTreeSettings{},originalOrnamentAttributes{},originalOrnamentSettings{};
unsigned char* host{};

bool ExtendRoute(void* self) {
    // RVA 8d2a20 resolves defaults/progress blocks into these fields, then
    // forwards the same values to the terrain and world visibility systems.
    auto terrain=Field<void*>(self,0x6b4),world=Field<void*>(self,0x6c8);
    const float clip=Field<float>(self,0x764);
    lod::Scenery ranges{Field<float>(self,0x75c),Field<float>(self,0x760),
        Field<float>(self,0x770),Field<float>(self,0x778)};
    if(!terrain || !world || !std::isfinite(clip) || clip<1 || !lod::Extend(ranges)) return false;
    Field<float>(self,0x75c)=ranges.terrainCull;
    Field<float>(self,0x760)=ranges.worldCull;
    Field<float>(self,0x770)=ranges.terrainDetail;
    Field<float>(self,0x778)=ranges.objectSize;
    // Do not touch world clip, shadow/reflection ranges, placements or PVS.
    Field<float>(terrain,0x5820)=ranges.terrainCull;
    Field<float>(terrain,0x5818)=ranges.terrainDetail;
    auto table=Field<void**>(world,0);
    reinterpret_cast<WorldDistanceFn>(table[1])(world,ranges.worldCull,clip);
    return true;
}
void __fastcall RouteUpdate(void* self,void*,float step) {
    originalRoute(self,step);
    // The original routine is a no-op when this native active-state byte is 0.
    if(!host[0x10476f9]) return;
    const lod::Scenery before{Field<float>(self,0x75c),Field<float>(self,0x760),
        Field<float>(self,0x770),Field<float>(self,0x778)};
    if(ExtendRoute(self)) {
        static thread_local unsigned samples{};
        if(samples++%600==0) Log("VR scenery range terrain=%.1f->%.1f world=%.1f->%.1f detail=%.1f->%.1f object=%.5f->%.5f",
            before.terrainCull,Field<float>(self,0x75c),before.worldCull,Field<float>(self,0x760),
            before.terrainDetail,Field<float>(self,0x770),before.objectSize,Field<float>(self,0x778));
    }
}
bool ExtendGrass(void* self) {
    // 4cd1c0 consumes these settings before allocating and populating ground
    // cover. Never enlarge counts after the native buffers have been created.
    if(!Field<unsigned char>(self,0x9c) || !Field<void*>(self,0xa8)) return false;
    auto& distance=Field<float>(self,0x53c);
    auto& items=Field<int>(self,0x544);
    auto& zones=Field<int>(self,0x548);
    if(!std::isfinite(distance) || distance<=0 || items<=0 || items>50000 || zones<=0 || zones>300) return false;
    const float before=distance; const int oldItems=items,oldZones=zones;
    distance=std::max(distance,250.f);
    // Stay within the stock reader's explicit 50,000-item / 300-zone limits.
    items=50000; zones=300;
    if(distance!=before || items!=oldItems || zones!=oldZones)
        Log("VR vegetation grass range=%.1f->%.1f items=%d->%d zones=%d->%d",before,distance,oldItems,items,oldZones,zones);
    return true;
}
bool __fastcall GrassLoad(void* self,void*) {
    ExtendGrass(self);
    return originalGrass(self);
}
constexpr unsigned grassItems=50000,grassIndexCount=grassItems*6;
template<class Index> void FillGrassIndices(Index* output,unsigned quads) {
    for(unsigned i=0;i<quads;++i) {
        const unsigned v=i*4;
        const unsigned pattern[]{v,v+1,v+3,v,v+3,v+2};
        for(unsigned j=0;j<6;++j) output[i*6+j]=static_cast<Index>(pattern[j]);
    }
}
bool GrassIndicesReady(void* self) {
    auto source=Field<void*>(self,0x760);
    return host && source && Field<void*>(source,0x3c)==host+0x106c890 &&
        Field<unsigned>(source,0x48)>=grassIndexCount;
}
bool ConfigureGrassIndices(void* self,bool wide) {
    auto source=Field<void*>(self,0x760),renderer=Field<void*>(self,0x718);
    if(!source || !renderer) return false;
    auto sourceTable=Field<void**>(source,0),rendererTable=Field<void**>(renderer,0);
    // Follow 9622a0's native configure/map/commit sequence. Its original fixed
    // ushort buffer contains only 65,535 indices (10,922 quads). A larger view
    // also requires a larger uint index buffer, not just more CPU vertices.
    using ModeFn=int(__thiscall*)(void*,bool);
    using ConfigureFn=int(__thiscall*)(void*,void*,unsigned,unsigned);
    using CountFn=void(__thiscall*)(void*,unsigned);
    using MapFn=void*(__thiscall*)(void*,void*);
    using CommitFn=void(__thiscall*)(void*,void*);
    if(reinterpret_cast<ModeFn>(sourceTable[0x24/4])(source,false)!=0) return false;
    const unsigned count=wide ? grassIndexCount : 0xffff;
    if(reinterpret_cast<ConfigureFn>(sourceTable[0x2c/4])(source,host+(wide ? 0x106c890 : 0x106c910),count,4)!=0) return false;
    reinterpret_cast<CountFn>(sourceTable[0x40/4])(source,count);
    auto data=reinterpret_cast<MapFn>(rendererTable[0x68/4])(renderer,source);
    if(!data) return false;
    if(wide) FillGrassIndices(static_cast<unsigned*>(data),grassItems);
    else FillGrassIndices(static_cast<unsigned short*>(data),0x2aaa);
    reinterpret_cast<CommitFn>(rendererTable[0x78/4])(renderer,source);
    return true;
}
void __fastcall GrassIndices(void* self,void*) {
    originalGrassIndices(self);
    if(ConfigureGrassIndices(self,true)) {
        Log("VR vegetation grass indices=300000 format=uint32 capacity=50000 clumps");
    } else {
        // A failed larger allocation must not leave a large view pointing into
        // the stock short buffer. Restore stock indices; the view guard below
        // then leaves its original allocation alone.
        Log("VR vegetation extended index allocation failed; restoring stock grass budget");
        if(!ConfigureGrassIndices(self,false)) ExitProcess(ERROR_NOT_ENOUGH_MEMORY);
    }
}
bool ExtendGrassViewBudget(void* self,void* settings) {
    // 981e10 creates the first free view; slot 0 is the main scene. Its
    // 16-byte settings are copied by 981ca0 BEFORE the selection array and
    // vertex buffers are allocated. The shared cache's maxitems is separate.
    if(!settings || Field<void*>(self,0x6f0) || !GrassIndicesReady(self)) return false;
    auto& items=Field<int>(settings,4);
    if(items<=0 || items>=50000) return false;
    items=50000;
    return true;
}
void* __fastcall GrassCreateView(void* self,void*,void* settings) {
    if(!settings) return originalGrassCreateView(self,settings);
    std::array<unsigned char,16> local{};
    memcpy(local.data(),settings,local.size());
    const bool changed=ExtendGrassViewBudget(self,local.data());
    // Do not mutate the caller's settings or resize any live buffer.
    auto view=originalGrassCreateView(self,changed ? local.data() : settings);
    if(changed && view && Field<void*>(self,0x6f0)==view)
        Log("VR vegetation main-view grass budget=%d->%d selected_capacity=%d vertices=%d",
            Field<int>(settings,4),Field<int>(view,0x14),Field<int>(view,0x28),Field<int>(view,0x2c));
    return view;
}
bool ExtendGrassView(void* self,void* view) {
    // 98e5e0 registers the view immediately before 98e570 consumes its cutoff
    // for cell selection and forwards it to the grass-generation workers.
    // Slot 0 is the main scene; slot 1 is the separate rear-view mirror.
    if(!view || Field<void*>(self,0x6f0)!=view) return false;
    auto& distance=Field<float>(view,4);
    if(!std::isfinite(distance) || distance<=0) return false;
    const float before=distance;
    distance=std::max(distance,250.f);
    static thread_local unsigned samples{};
    if(samples++%600==0) Log("VR vegetation main-view grass cutoff=%.1f->%.1f",before,distance);
    return true;
}
void __fastcall GrassView(void* self,void*,void* view,void* matrix,void* position,int count,void* cells) {
    ExtendGrassView(self,view);
    originalGrassView(self,view,matrix,position,count,cells);
}
int ExtendTreeAttributes(void* self) {
    auto records=Field<unsigned char*>(self,0xf8);
    const int count=Field<int>(self,0xfc);
    if(!records || count<=0 || count>1024) return 0;
    int changed=0;
    for(int i=0;i<count;++i) {
        auto ranges=&Field<float>(records+i*0x40,0x24);
        const std::array<float,3> before{ranges[0],ranges[1],ranges[2]};
        if(lod::TreeRanges(ranges) && !std::equal(before.begin(),before.end(),ranges)) ++changed;
    }
    auto& landmark=Field<float>(self,0x17c);
    if(std::isfinite(landmark) && landmark>0) landmark=std::max(landmark,1000.f);
    return changed;
}
void __fastcall TreeAttributes(void* self,void*,void* xml) {
    originalTreeAttributes(self,xml);
    const int changed=ExtendTreeAttributes(self);
    if(changed) Log("VR vegetation tree/bush LOD floors=80/300/1000m changed=%d",changed);
}
int ExtendTreeSettings(void* self) {
    auto styles=Field<unsigned char*>(self,0x180);
    const int count=Field<int>(self,0x184);
    if(!styles || count<=0 || count>64) return 0;
    int changed=0;
    for(int i=0;i<count;++i) {
        // 96dd20: 0x78-byte style, main_scene traversal index 0. Other
        // traversal budgets (shadows, reflections, mirrors) stay untouched.
        auto& instances=Field<int>(styles+i*0x78,0x20);
        auto& billboards=Field<int>(styles+i*0x78,0x4c);
        if(instances<0 || instances>100000 || billboards<0 || billboards>100000) continue;
        const int oldInstances=instances,oldBillboards=billboards;
        if(instances>0) instances=std::max(instances,2000);
        if(billboards>0) billboards=std::max(billboards,3000);
        if(instances!=oldInstances || billboards!=oldBillboards) ++changed;
    }
    return changed;
}
void __fastcall TreeSettings(void* self,void*,void* xml) {
    originalTreeSettings(self,xml);
    const int changed=ExtendTreeSettings(self);
    if(changed) Log("VR vegetation main-scene tree capacity floors=2000/3000 changed_styles=%d",changed);
}
int ExtendOrnamentAttributes(void* self) {
    // 959170 allocates 0x58-byte records, then reads linear distances at +24.
    // 958d60/983674 copy them into the native prop types before scene traversal.
    auto records=Field<unsigned char*>(self,0x44);
    const int count=Field<int>(self,0x48);
    if(!records || count<=0 || count>4096) return 0;
    int changed=0;
    for(int i=0;i<count;++i) {
        auto record=records+i*0x58;
        // Reflection-only objects retain their own policy.
        if(Field<unsigned char>(record,0x31)) continue;
        auto ranges=&Field<float>(record,0x24);
        const std::array<float,3> before{ranges[0],ranges[1],ranges[2]};
        if(lod::OrnamentRanges(ranges) && !std::equal(before.begin(),before.end(),ranges)) ++changed;
    }
    return changed;
}
void __fastcall OrnamentAttributes(void* self,void*,void* xml) {
    originalOrnamentAttributes(self,xml);
    const int changed=ExtendOrnamentAttributes(self);
    if(changed) Log("VR trackside prop LOD floors=80/300/1000m changed=%d",changed);
}
int ExtendOrnamentSettings(void* self) {
    // 967ad0 reads 0x50-byte styles. 982df4 copies their 12 traversal budgets
    // before allocating instance buffers. Only raise main_scene (index 0).
    auto styles=Field<unsigned char*>(self,0x468);
    const int count=Field<int>(self,0x46c);
    if(!styles || count<=0 || count>64) return 0;
    int changed=0;
    for(int i=0;i<count;++i) {
        auto& capacity=Field<int>(styles+i*0x50,0x20);
        if(capacity>0 && capacity<2000) {capacity=2000;++changed;}
    }
    return changed;
}
void __fastcall OrnamentSettings(void* self,void*,void* xml) {
    originalOrnamentSettings(self,xml);
    const int changed=ExtendOrnamentSettings(self);
    if(changed) Log("VR trackside prop main-scene capacity floor=2000 changed_styles=%d",changed);
}
struct NearCar {void* car{}; bool close{};};
void KeepNearbyCars(void* self,std::array<NearCar,8>& state) {
    const int count=Field<int>(self,0x1248),profile=Field<int>(self,0x1dd4);
    // Race profiles only: replay and frontend tables keep their own policy.
    if(count<0 || count>8 || profile<0 || profile>3) {state={};return;}
    const auto eye=&Field<float>(self,0x1b30);
    for(int i=0;i<8;++i) {
        auto car=i<count ? Field<void*>(self,0x1788+i*4) : nullptr;
        if(!car) {state[i]={};continue;}
        const int best=Field<int>(car,0x134),worst=Field<int>(car,0x130);
        if(best<0 || worst<best || worst>7) {state[i]={};continue;}
        const bool previous=state[i].car==car && state[i].close;
        state[i]={car,lod::Nearby(eye,&Field<float>(car,0x80),previous)};
        auto& selected=Field<int>(self,0x1b4c+i*4);
        if(state[i].close && selected>best && selected<=worst) {
            selected=best;
        }
    }
}
void __fastcall CarApply(void* self,void*) {
    // This is the final selection step, after the forward-frustum/distance and
    // car-count budgets, before meshes change. Leave all other callers alone.
    if(_ReturnAddress()==host+0x9cc444) {
        static thread_local std::array<NearCar,8> state{};
        static thread_local void* manager{};
        if(manager!=self) {state={};manager=self;}
        std::array<int,8> before{};
        for(int i=0;i<8;++i) before[i]=Field<int>(self,0x1b4c+i*4);
        KeepNearbyCars(self,state);
        static thread_local unsigned samples{};
        if(samples++%600==0) {
            const int count=Field<int>(self,0x1248);
            if(count>=0 && count<=8) for(int i=0;i<count;++i)
                Log("VR car detail slot=%d nearby=%d lod=%d->%d",i,state[i].close,before[i],Field<int>(self,0x1b4c+i*4));
        }
    }
    originalCars(self);
}
}
bool EnableDrawDistance() {
    static bool ready{};
    if(ready) return true;
    if(!SupportedHost()) return false;
    host=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    // Validate the variable absolute operand separately so ASLR stays supported.
    const unsigned char route[]={0x83,0xec,0x20,0x84,0xc0,0x56,0x8b,0xf1};
    const unsigned char cars[]={0x51,0x53,0x8b,0xd9,0x8b,0x83,0x48,0x12,0,0,0x57,0x33,0xff};
    const unsigned char applyCall[]={0xe8,0x0c,0xbe,0xfe,0xff};
    if(host[0x8d2a20]!=0xa0 || Field<uintptr_t>(host,0x8d2a21)!=reinterpret_cast<uintptr_t>(host+0x10476f9) ||
       memcmp(host+0x8d2a25,route,sizeof(route)) || memcmp(host+0x9b8250,cars,sizeof(cars)) ||
       memcmp(host+0x9cc43f,applyCall,sizeof(applyCall))) return false;
    const unsigned char grass[]={0x83,0xec,0x20,0x56,0x8b,0xf1,0x80,0xbe,0x9c,0,0,0,0};
    const unsigned char grassIndices[]={0x81,0xec,0x88,0,0,0,0x53,0x56,0x57,0x8b,0xf1};
    const unsigned char grassCreateView[]={0x55,0x56,0x8b,0xf1,0x8d,0xae,0xf8,0x06,0,0};
    const unsigned char grassView[]={0x56,0x8b,0xf1,0x83,0x7e,0x04,0x00,0x74,0x7a};
    const unsigned char attributes[]={0x83,0xec,0x2c,0x56,0x57,0x8b,0x7c,0x24,0x38,0x8b,0xf1};
    const unsigned char settings[]={0x83,0xec,0x58,0x56,0x8b,0x74,0x24,0x60,0x57,0x8b,0xf9};
    const unsigned char ornamentAttributes[]={0x83,0xec,0x30,0x56,0x57,0x8b,0x7c,0x24,0x3c,0x8b,0xf1};
    if(memcmp(host+0x959170,ornamentAttributes,sizeof(ornamentAttributes)) ||
       memcmp(host+0x967ad0,settings,sizeof(settings))) return false;
    if(memcmp(host+0x9622a0,grassIndices,sizeof(grassIndices)) || memcmp(host+0x981e10,grassCreateView,sizeof(grassCreateView)) || memcmp(host+0x98e5e0,grassView,sizeof(grassView)) || memcmp(host+0x4cd1c0,grass,sizeof(grass)) || memcmp(host+0x95bd70,attributes,sizeof(attributes)) ||
       memcmp(host+0x96dd20,settings,sizeof(settings))) return false;
    auto hook=[&](size_t rva,void* replacement,void** original) {
        auto result=MH_CreateHook(host+rva,replacement,original);
        if(result==MH_OK) result=EnableRecordedHook(host+rva);
        Log("VR draw-distance hook RVA=0x%zx status=%s",rva,MH_StatusToString(result));
        return result==MH_OK;
    };
    ready=hook(0x8d2a20,reinterpret_cast<void*>(RouteUpdate),reinterpret_cast<void**>(&originalRoute)) &&
        hook(0x9b8250,reinterpret_cast<void*>(CarApply),reinterpret_cast<void**>(&originalCars)) &&
        hook(0x4cd1c0,reinterpret_cast<void*>(GrassLoad),reinterpret_cast<void**>(&originalGrass)) &&
        hook(0x9622a0,reinterpret_cast<void*>(GrassIndices),reinterpret_cast<void**>(&originalGrassIndices)) &&
        hook(0x981e10,reinterpret_cast<void*>(GrassCreateView),reinterpret_cast<void**>(&originalGrassCreateView)) &&
        hook(0x98e5e0,reinterpret_cast<void*>(GrassView),reinterpret_cast<void**>(&originalGrassView)) &&
        hook(0x95bd70,reinterpret_cast<void*>(TreeAttributes),reinterpret_cast<void**>(&originalTreeAttributes)) &&
        hook(0x96dd20,reinterpret_cast<void*>(TreeSettings),reinterpret_cast<void**>(&originalTreeSettings)) &&
        hook(0x959170,reinterpret_cast<void*>(OrnamentAttributes),reinterpret_cast<void**>(&originalOrnamentAttributes)) &&
        hook(0x967ad0,reinterpret_cast<void*>(OrnamentSettings),reinterpret_cast<void**>(&originalOrnamentSettings));
    return ready;
}
}
