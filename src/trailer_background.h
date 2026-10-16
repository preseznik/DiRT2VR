#pragma once
#include <array>
#include <cstdint>
#include <functional>
#include <d3d11.h>
#include <wrl/client.h>
namespace vr {
struct TrailerBackgroundRays { std::array<float,12> values{}; bool valid{}; };
TrailerBackgroundRays TrailerBackgroundTransform(const float* nativeView,const float* nativeProjection,
    const float* eyeView,const float* eyeProjection);
bool TrailerBackgroundCoversView(const TrailerBackgroundRays&);
class TrailerBackground {
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> shader_;
    Microsoft::WRL::ComPtr<ID3D11Buffer> constants_;
    bool attempted_{};
public:
    bool Draw(ID3D11DeviceContext*,uint64_t vertex,uint64_t pixel,const TrailerBackgroundRays&,const std::function<void()>&);
};
}
