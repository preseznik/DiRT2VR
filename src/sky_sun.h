#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include <cstdint>
#include <functional>

namespace vr {
class SkySunPass {
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> shader_;
    Microsoft::WRL::ComPtr<ID3D11VertexShader> vertex_;
    bool attempted_{};
public:
    bool Draw(ID3D11DeviceContext* context,uint64_t vertex,uint64_t pixel,const std::function<void()>& draw);
};
}
