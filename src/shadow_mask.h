#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include <array>
#include <cmath>
#include <cstdint>
#include <functional>
namespace vr {
struct ShadowRays { std::array<float,4> values{}; bool valid{}; };
inline ShadowRays ShadowViewRays(const float* p) {
    if(!p || p[11]!=-1.f || p[15]!=0.f || !std::isfinite(p[0]) || !std::isfinite(p[5]) ||
       p[0]<.01f || p[5]<.01f || !std::isfinite(p[8]) || !std::isfinite(p[9])) return {};
    ShadowRays rays{{1.f/p[0],1.f/p[5],p[8]/p[0],p[9]/p[5]},true};
    for(float value:rays.values) if(!std::isfinite(value)) return {};
    return rays;
}
class ShadowMaskPass {
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11VertexShader> shader_;
    Microsoft::WRL::ComPtr<ID3D11Buffer> constants_;
    bool attempted_{};
public:
    bool Draw(ID3D11DeviceContext* context,uint64_t vertex,uint64_t pixel,const ShadowRays& rays,const std::function<void()>& draw);
};
}
