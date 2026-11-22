#pragma once
#include <d3d11_1.h>
#include <wrl/client.h>

namespace vr {
// Draws a premultiplied-alpha texture onto a render target with proper blending,
// scaled into a rectangle, without disturbing the game's pipeline state.
class OverlayBlit {
public:
    // coverage: also build up the target's alpha (for a transparent layer drawn
    // later with its own alpha); otherwise the frame's alpha is left as it was.
    explicit OverlayBlit(bool coverage=false):coverage_(coverage) {}
    bool Draw(ID3D11RenderTargetView* target,ID3D11Texture2D* image,float x,float y,float width,float height);
private:
    bool Prepare(ID3D11Device* device);
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext1> context_;
    Microsoft::WRL::ComPtr<ID3DDeviceContextState> state_;
    Microsoft::WRL::ComPtr<ID3D11VertexShader> vs_;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> ps_;
    Microsoft::WRL::ComPtr<ID3D11BlendState> blend_;
    Microsoft::WRL::ComPtr<ID3D11RasterizerState> raster_;
    Microsoft::WRL::ComPtr<ID3D11DepthStencilState> depth_;
    Microsoft::WRL::ComPtr<ID3D11SamplerState> sampler_;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> source_;
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> view_;
    bool ready_{},attempted_{},coverage_{};
};
}
