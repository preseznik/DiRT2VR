#pragma once
#include <windows.h>
#include <d3d11.h>
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <wrl/client.h>
#include <memory>
#include <vector>
#include "performance_stats.h"

namespace vr {
struct GpuCounterState;
// Windows adapter counters sampled once per second on a worker that owns no
// D3D/XR objects. Stop signals the worker and never waits for it.
class GpuUsageSampler {
public:
    GpuUsageSampler()=default;
    GpuUsageSampler(const GpuUsageSampler&)=delete;
    GpuUsageSampler& operator=(const GpuUsageSampler&)=delete;
    ~GpuUsageSampler() {Stop();}
    bool Start(ID3D11Device* device);
    double Percent() const;
    void Stop();
private:
    std::shared_ptr<GpuCounterState> state_;
};
// Optional FPS/GPU panel in the headset (Advanced -> FPS + GPU usage), ported
// from GRID2VR. It is an independent compositor quad that follows the view: no
// game shader, render target or HUD is changed. All XR/GPU calls remain on the
// thread that initialized it.
class PerformanceDisplay {
public:
    static bool Requested();
    bool Initialize(XrSession session,ID3D11Device* device);
    const XrCompositionLayerQuad* Layer(uint64_t milliseconds);
    void Submitted(uint64_t milliseconds) {if(enabled_)rate_.Frame(milliseconds);}
    bool Shutdown();
private:
    bool Upload(uint64_t milliseconds);
    XrSpace view_{};
    XrSwapchain chain_{};
    XrCompositionLayerQuad layer_{XR_TYPE_COMPOSITION_LAYER_QUAD};
    std::vector<XrSwapchainImageD3D11KHR> images_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
    Microsoft::WRL::ComPtr<ID3D11Query> completion_;
    SubmittedFrameRate rate_;
    GpuUsageSampler gpu_;
    DWORD thread_{};
    uint64_t updated_{};
    uint32_t index_{};
    bool enabled_{},ready_{},used_{},acquired_{},waited_{};
};
}
