#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include <array>
#include <cstdint>
#include <vector>

// Bounded asynchronous timestamp ring. Never waits or flushes the game context.
class GpuTimer {
    struct Slot {
        Microsoft::WRL::ComPtr<ID3D11Query> disjoint,start,end;
        uint64_t frame{};
        bool pending{},valid{};
    };
    std::array<Slot,8> slots_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
    Slot* active_{};
public:
    struct Sample { uint64_t frame; double milliseconds; bool valid; };
    bool Initialize(ID3D11Device* device) {
        if(!device || context_) return false;
        for(auto& slot:slots_) {
            D3D11_QUERY_DESC desc{D3D11_QUERY_TIMESTAMP_DISJOINT,0};
            if(FAILED(device->CreateQuery(&desc,&slot.disjoint))) return false;
            desc.Query=D3D11_QUERY_TIMESTAMP;
            if(FAILED(device->CreateQuery(&desc,&slot.start)) || FAILED(device->CreateQuery(&desc,&slot.end))) return false;
        }
        device->GetImmediateContext(&context_); return true;
    }
    bool Begin(uint64_t frame) {
        if(!context_ || active_) return false;
        for(auto& slot:slots_) if(!slot.pending) {
            active_=&slot; slot.frame=frame;
            context_->Begin(slot.disjoint.Get()); context_->End(slot.start.Get()); return true;
        }
        return false; // GPU has not retired the ring; skip sampling, never stall.
    }
    void End(bool valid=true) {
        if(!active_) return;
        context_->End(active_->end.Get()); context_->End(active_->disjoint.Get());
        active_->pending=true; active_->valid=valid; active_=nullptr;
    }
    std::vector<Sample> Poll() {
        std::vector<Sample> result;
        if(!context_) return result;
        for(auto& slot:slots_) if(slot.pending) {
            D3D11_QUERY_DATA_TIMESTAMP_DISJOINT clock{}; UINT64 start{},end{};
            const auto a=context_->GetData(slot.disjoint.Get(),&clock,sizeof(clock),D3D11_ASYNC_GETDATA_DONOTFLUSH);
            const auto b=context_->GetData(slot.start.Get(),&start,sizeof(start),D3D11_ASYNC_GETDATA_DONOTFLUSH);
            const auto c=context_->GetData(slot.end.Get(),&end,sizeof(end),D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if(a==S_FALSE || b==S_FALSE || c==S_FALSE) continue;
            const bool valid=SUCCEEDED(a) && SUCCEEDED(b) && SUCCEEDED(c) && slot.valid && !clock.Disjoint && clock.Frequency && end>=start;
            result.push_back({slot.frame,valid ? 1000.0*(end-start)/clock.Frequency : 0,valid});
            slot.pending=false;
        }
        return result;
    }
};
