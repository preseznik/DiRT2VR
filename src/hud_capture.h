#pragma once
#include <d3d11_1.h>
#include <wrl/client.h>
#include <cstdint>
#include <functional>

// The caller identifies EGO HUD shaders. Capture their backbuffer draws without
// executing game UI logic again, and restore all altered graphics state.
class HudCapture {
public:
    bool Begin(ID3D11Texture2D* back,uint64_t frame,unsigned hidden=0);
    bool Draw(ID3D11DeviceContext* context,const std::function<void()>& draw,bool capture=true);
    void End(bool cockpit);
    ID3D11Texture2D* Current(uint64_t frame) const;
    unsigned Draws() const { return draws_; }
    float Aspect() const { return height_ ? float(width_)/height_ : 1.f; }
private:
    Microsoft::WRL::ComPtr<ID3D11Texture2D> back_,image_;
    Microsoft::WRL::ComPtr<ID3D11RenderTargetView> target_;
    Microsoft::WRL::ComPtr<ID3D11BlendState> blend_;
    Microsoft::WRL::ComPtr<ID3D11DepthStencilState> depth_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext1> context1_;
    D3D11_BLEND_DESC blendDesc_{};
    D3D11_DEPTH_STENCIL_DESC depthDesc_{};
    uint64_t frame_{};
    unsigned width_{},height_{},draws_{},hidden_{};
    bool active_{},ready_{};
};
