#include "terrain_registry.h"
#include "common.h"
#include "pikes_scope.h"
#include <MinHook.h>
#include <Windows.h>
#include <bcrypt.h>
#include <intrin.h>
#include <array>
#include <cstring>
#include <fstream>
#include <mutex>
#include <string>

namespace vr {
namespace {
constexpr unsigned capacity=4096,oldCapacity=1280;
constexpr unsigned appendRva=0xcb9a40,writeOperandRva=0xcb9a55,readOperandRva=0xca0beb;
constexpr unsigned registryRva=0x1289130,countRva=0x128b5dc,gridRva=0x128a530,vehicleRva=0x128b530;
alignas(16) std::array<uintptr_t,capacity> registry{};
std::array<uintptr_t,1024> originalGrid{};
std::array<uintptr_t,35> originalVehicles{};
unsigned originalVehicleCount{},expectedChunks{};
unsigned char* hostBase{};
bool relocated{},closing{};
std::mutex mutex;
using RegisterFn=void(__cdecl*)(void*,int,int,int,int);
RegisterFn realRegister{};

template<class T> bool Read(uintptr_t at,T& value) {
    SIZE_T bytes{};
    return at>0x10000 && ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(at),&value,sizeof(value),&bytes) && bytes==sizeof(value);
}
bool ValidOperands(uintptr_t table) {
    auto write=hostBase+writeOperandRva,read=hostBase+readOperandRva;
    const unsigned char writeOpcode[]{0x89,0x1c,0x85},readOpcode[]{0x8b,0x0d};
    return !memcmp(write-3,writeOpcode,sizeof(writeOpcode)) && !memcmp(read-2,readOpcode,sizeof(readOpcode)) &&
        !memcmp(write,&table,sizeof(table)) && !memcmp(read,&table,sizeof(table));
}
bool SetOperand(unsigned rva,uintptr_t from,uintptr_t to) {
    auto at=hostBase+rva;if(memcmp(at,&from,sizeof(from)))return false;
    DWORD protection{},ignored{};if(!VirtualProtect(at,sizeof(to),PAGE_EXECUTE_READWRITE,&protection))return false;
    memcpy(at,&to,sizeof(to));const bool protect=VirtualProtect(at,sizeof(to),protection,&ignored)!=0;
    const bool flush=FlushInstructionCache(GetCurrentProcess(),at,sizeof(to))!=0;
    Log("pikes-terrain-registry operand-rva=%08x old=%08x new=%08x protect-restored=%d flushed=%d",rva,static_cast<unsigned>(from),static_cast<unsigned>(to),protect,flush);
    return protect && flush;
}
void Neighbors(unsigned count,const char* phase) {
    std::array<uintptr_t,1024> grid{};std::array<uintptr_t,35> vehicles{};unsigned vehicleCount{};
    const bool gridValid=Read(reinterpret_cast<uintptr_t>(hostBase)+gridRva,grid);
    const bool vehiclesValid=Read(reinterpret_cast<uintptr_t>(hostBase)+vehicleRva,vehicles)&&Read(reinterpret_cast<uintptr_t>(hostBase)+0x128b5bc,vehicleCount);
    unsigned gridChanged{},terrainAliases{};
    for(unsigned i=0;i<grid.size();++i)if(grid[i]!=originalGrid[i])++gridChanged;
    for(auto vehicle:vehicles)if(vehicle)for(unsigned i=0;i<count && i<capacity;++i)if(vehicle==registry[i]){++terrainAliases;break;}
    Log("pikes-terrain-registry %s count=%u expected=%u entry0=%08x last=%08x operands-valid=%d grid-valid=%d grid-pointer-changes=%u grid-first=%08x original-grid-first=%08x grid-last=%08x original-grid-last=%08x vehicle-valid=%d vehicle-count=%u original-vehicle-count=%u vehicle0=%08x original-vehicle0=%08x terrain-aliases-in-vehicle-table=%u vehicle-table-same=%d",
        phase,count,expectedChunks,static_cast<unsigned>(registry[0]),count&&count<=capacity?static_cast<unsigned>(registry[count-1]):0,
        ValidOperands(reinterpret_cast<uintptr_t>(registry.data())),gridValid,gridChanged,static_cast<unsigned>(grid.front()),static_cast<unsigned>(originalGrid.front()),static_cast<unsigned>(grid.back()),static_cast<unsigned>(originalGrid.back()),vehiclesValid,vehicleCount,originalVehicleCount,
        static_cast<unsigned>(vehicles[0]),static_cast<unsigned>(originalVehicles[0]),terrainAliases,vehicles==originalVehicles);
}
void Register(void* shape,int a,int b,int c,int d) {
    const auto caller=reinterpret_cast<uintptr_t>(_ReturnAddress())-reinterpret_cast<uintptr_t>(hostBase);
    std::lock_guard lock(mutex);
    if(closing)return; // No further registration while the owned process is exiting.
    unsigned count{};
    if(!Read(reinterpret_cast<uintptr_t>(hostBase)+countRva,count) || caller!=0x290256 || count>=capacity) {
        Log("pikes-terrain-registry rejected unsafe append caller-rva=%08x count=%u",static_cast<unsigned>(caller),count);
        TerminateProcess(GetCurrentProcess(),ERROR_ARITHMETIC_OVERFLOW);return;
    }
    if(relocated && count==0) {
        registry.fill(0);
        Read(reinterpret_cast<uintptr_t>(hostBase)+gridRva,originalGrid);
        Read(reinterpret_cast<uintptr_t>(hostBase)+vehicleRva,originalVehicles);
        Read(reinterpret_cast<uintptr_t>(hostBase)+0x128b5bc,originalVehicleCount);
        Log("pikes-terrain-registry new world native-count=0 private-slots-reset=4096 operands-retained=1");
    }
    if(!relocated) {
        const auto old=reinterpret_cast<uintptr_t>(hostBase)+registryRva,next=reinterpret_cast<uintptr_t>(registry.data());
        if(count!=0 || !ValidOperands(old) || !Read(reinterpret_cast<uintptr_t>(hostBase)+gridRva,originalGrid) ||
           !Read(reinterpret_cast<uintptr_t>(hostBase)+vehicleRva,originalVehicles) || !Read(reinterpret_cast<uintptr_t>(hostBase)+0x128b5bc,originalVehicleCount)) {
            Log("pikes-terrain-registry refused nonzero-count/operand/neighbor guard count=%u",count);
            TerminateProcess(GetCurrentProcess(),ERROR_BAD_EXE_FORMAT);return;
        }
        if(!SetOperand(writeOperandRva,old,next) || !SetOperand(readOperandRva,old,next) || !ValidOperands(next)) {
            Log("pikes-terrain-registry operand update failed; no native registration performed");
            TerminateProcess(GetCurrentProcess(),ERROR_BAD_EXE_FORMAT);return;
        }
        relocated=true;
        Log("pikes-terrain-registry relocated count-before=0 old=%08x private=%08x capacity=%u expected-chunks=%u old-table-copy=0",static_cast<unsigned>(old),static_cast<unsigned>(next),capacity,expectedChunks);
    }
    realRegister(shape,a,b,c,d);
    unsigned after{};
    if(!Read(reinterpret_cast<uintptr_t>(hostBase)+countRva,after) || after!=count+1 || registry[count]!=reinterpret_cast<uintptr_t>(shape)) {
        Log("pikes-terrain-registry append consistency failed before=%u after=%u slot=%08x shape=%p",count,after,static_cast<unsigned>(registry[count]),shape);
        TerminateProcess(GetCurrentProcess(),ERROR_BAD_EXE_FORMAT);return;
    }
    if(after==1 || after==oldCapacity || after==oldCapacity+1 || after==2304 || after==2305 || after==expectedChunks)Neighbors(after,after==expectedChunks?"complete":"checkpoint");
}
}
bool EnablePikesTerrain() {
    if(!PikesScope())return true;
    if(!SupportedHost())return false;
    static bool attempted{},armed{};if(attempted)return armed;attempted=true;
    expectedChunks=PikesChunks();
    auto initStatus=MH_Initialize();
    if(initStatus!=MH_OK && initStatus!=MH_ERROR_ALREADY_INITIALIZED){Log("pikes-terrain-registry MinHook init refused");return false;}
    hostBase=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const auto countAddress=reinterpret_cast<uintptr_t>(hostBase)+countRva,old=reinterpret_cast<uintptr_t>(hostBase)+registryRva;
    std::array<unsigned char,25> append{0x55,0x8b,0xec,0x83,0xe4,0xf0,0xa1,0,0,0,0,0x83,0xec,0x54,0x53,0x8b,0x5d,0x08,0x89,0x1c,0x85};
    memcpy(append.data()+7,&countAddress,4);memcpy(append.data()+21,&old,4);
    std::array<unsigned char,20> reader{0x83,0x3d,0,0,0,0,0,0x7e,0x0b,0x8b,0x0d,0,0,0,0,0xe9,0x6c,0x33,0xf8,0xff};
    memcpy(reader.data()+2,&countAddress,4);memcpy(reader.data()+11,&old,4);
    // The launch transaction prepares this allocation before process creation.
    // Its separate terrain pointer registry must also be relocated before load.
    constexpr unsigned geometryRva=0x2c4bd0,capacityRva=geometryRva+7;
    constexpr std::array<unsigned char,11> geometry{0xc7,0x84,0x24,0xf4,0,0,0,0,0x10,0,0};
    unsigned count{},geomCapacity{};
    if(memcmp(hostBase+appendRva,append.data(),append.size()) || memcmp(hostBase+0xca0be0,reader.data(),reader.size()) ||
       !Read(countAddress,count) || count!=0 || memcmp(hostBase+geometryRva,geometry.data(),geometry.size()) ||
       !Read(reinterpret_cast<uintptr_t>(hostBase)+capacityRva,geomCapacity) || geomCapacity!=capacity) {
        Log("pikes-terrain-registry refused prepared byte/count-zero/geometry guard count=%u geom=%u",count,geomCapacity);return false;
    }
    auto status=MH_CreateHook(hostBase+appendRva,reinterpret_cast<void*>(Register),reinterpret_cast<void**>(&realRegister));
    if(status==MH_OK)status=MH_EnableHook(hostBase+appendRva);
    Log("pikes-terrain-registry armed hook=%s count=0 capacity=%u chunks=%u jpk-sha=%s full-operand-guards=1 disk-writes=0",MH_StatusToString(status),capacity,expectedChunks,PikesHash());
    armed=status==MH_OK;
    return armed;
}
}
