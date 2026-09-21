#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include <array>
#include <cstdint>

// Owns copies, never the game's backbuffer. A pair is usable only after both
// captures from the same sequence; resized/incomplete pairs cannot be submitted.
class EyePair {
public:
    HRESULT Capture(ID3D11Texture2D* source,unsigned eye,uint64_t sequence);
    bool Ready(uint64_t sequence) const { return mask_==3 && sequence_==sequence; }
    ID3D11Texture2D* Texture(unsigned eye) const { return eye<2 ? textures_[eye].Get() : nullptr; }
private:
    std::array<Microsoft::WRL::ComPtr<ID3D11Texture2D>,2> textures_;
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    D3D11_TEXTURE2D_DESC sourceDesc_{};
    uint64_t sequence_{};
    unsigned mask_{};
};
