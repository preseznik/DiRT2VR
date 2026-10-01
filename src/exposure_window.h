#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include <array>
#include <cstdint>
#include <functional>
namespace vr {
struct MeterWindow { std::array<float,4> bounds{}; bool valid{}; };
MeterWindow NativeMeterWindow(float originalX,float originalY,const float* projection);
class ExposureWindow {
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> shader_;
    Microsoft::WRL::ComPtr<ID3D11Buffer> constants_;
    bool attempted_{};
public:
    bool Draw(ID3D11DeviceContext*,uint64_t vertex,uint64_t pixel,const MeterWindow&,const std::function<void()>&);
};
}
