#pragma once
#include <d3d11_1.h>
#include <wrl/client.h>
#include <array>

// Copies/resizes a completed game eye into a compositor image while preserving
// the game's full immediate-context state (including bindings not used here).
class EyeBlit {
public:
    bool Initialize(ID3D11Device* device);
    bool Draw(unsigned eye,ID3D11Texture2D* source,ID3D11RenderTargetView* target,unsigned width,unsigned height);
private:
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext1> context_;
    Microsoft::WRL::ComPtr<ID3DDeviceContextState> state_;
    Microsoft::WRL::ComPtr<ID3D11VertexShader> vs_;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> ps_;
    Microsoft::WRL::ComPtr<ID3D11SamplerState> sampler_;
    Microsoft::WRL::ComPtr<ID3D11RasterizerState> raster_;
    Microsoft::WRL::ComPtr<ID3D11DepthStencilState> depth_;
    std::array<Microsoft::WRL::ComPtr<ID3D11Texture2D>,2> sources_;
    std::array<Microsoft::WRL::ComPtr<ID3D11ShaderResourceView>,2> views_;
};
