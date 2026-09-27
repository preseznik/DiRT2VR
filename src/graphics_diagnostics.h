#pragma once
#include <d3d11.h>
#include <atomic>
#include <algorithm>
#include <cstdint>
#include <cstdio>

// Opt-in and bounded: no per-frame success logging, allocations or exception handler.
namespace vr::graphics {
using Logger=void(*)(const char*);
inline std::atomic<Logger> logger{nullptr};
inline std::atomic<unsigned> failures{0}, allocations{0};
inline void Memory(const char* operation,const char* phase) {
    auto log=logger.load(); if(!log) return;
    SYSTEM_INFO info{}; GetSystemInfo(&info);
    const auto limit=uint64_t(reinterpret_cast<uintptr_t>(info.lpMaximumApplicationAddress))+1;
    uint64_t committed=0,reserved=0,free=0,largest=0,address=0;
    bool complete=true;
    while(address<limit) {
        MEMORY_BASIC_INFORMATION region{};
        if(!VirtualQuery(reinterpret_cast<void*>(static_cast<uintptr_t>(address)),&region,sizeof(region))) { complete=false; break; }
        const auto end=std::min(limit,uint64_t(reinterpret_cast<uintptr_t>(region.BaseAddress))+region.RegionSize);
        if(end<=address) { complete=false; break; }
        const auto size=end-address;
        if(region.State==MEM_COMMIT) committed+=size;
        else if(region.State==MEM_RESERVE) reserved+=size;
        else if(region.State==MEM_FREE) { free+=size; largest=std::max(largest,size); }
        address=end;
    }
    char message[512]{};
    snprintf(message,sizeof(message),"graphics memory operation=%s phase=%s committed=%llu reserved=%llu free=%llu largest_free=%llu limit=%llu complete=%d",
        operation,phase,committed,reserved,free,largest,limit,complete);
    log(message);
}
inline bool Failed(HRESULT hr,ID3D11Device* device,const char* operation,const D3D11_TEXTURE2D_DESC* desc=nullptr) {
    if(SUCCEEDED(hr)) return false;
    auto log=logger.load();
    if(log) {
        const auto index=failures.fetch_add(1);
        if(index<16) {
            char message[512]{};
            snprintf(message,sizeof(message),"graphics failure operation=%s hr=0x%08lx device_removed=0x%08lx width=%u height=%u format=%u samples=%u quality=%u",
                operation,hr,device ? device->GetDeviceRemovedReason() : S_OK,
                desc ? desc->Width : 0,desc ? desc->Height : 0,desc ? unsigned(desc->Format) : 0,
                desc ? desc->SampleDesc.Count : 0,desc ? desc->SampleDesc.Quality : 0);
            log(message); Memory(operation,"failure");
        } else if(index==16) log("graphics failure logging capped at 16 reports for this process");
    }
    return true;
}
class Allocation {
    const char* operation_;
    bool report_;
public:
    explicit Allocation(const char* operation):operation_(operation),report_(logger.load() && allocations.fetch_add(1)<16) {
        if(report_) Memory(operation_,"before");
    }
    ~Allocation() { if(report_) Memory(operation_,"after"); }
    Allocation(const Allocation&)=delete;
    Allocation& operator=(const Allocation&)=delete;
};
}
