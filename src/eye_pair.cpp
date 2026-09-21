#include "eye_pair.h"
using Microsoft::WRL::ComPtr;

HRESULT EyePair::Capture(ID3D11Texture2D* source,unsigned eye,uint64_t sequence) {
    if(!source || eye>1) { mask_=0; return E_INVALIDARG; }
    if(eye==0) mask_=0;
    D3D11_TEXTURE2D_DESC desc{}; source->GetDesc(&desc);
    if(desc.MipLevels!=1 || desc.ArraySize!=1 ||
       (desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM && desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB &&
        desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM)) { mask_=0; return E_INVALIDARG; }
    ComPtr<ID3D11Device> device; source->GetDevice(&device);
    const bool changed=device_!=device || sourceDesc_.Width!=desc.Width || sourceDesc_.Height!=desc.Height ||
        sourceDesc_.Format!=desc.Format || sourceDesc_.SampleDesc.Count!=desc.SampleDesc.Count ||
        sourceDesc_.SampleDesc.Quality!=desc.SampleDesc.Quality;
    if(eye==1 && (changed || mask_!=1 || sequence_!=sequence)) { mask_=0; return E_INVALIDARG; }
    if(changed || !textures_[0]) {
        auto target=desc; target.SampleDesc={1,0}; target.Usage=D3D11_USAGE_DEFAULT;
        target.BindFlags=D3D11_BIND_SHADER_RESOURCE; target.CPUAccessFlags=0; target.MiscFlags=0;
        std::array<ComPtr<ID3D11Texture2D>,2> replacement;
        for(auto& texture:replacement) {
            const auto hr=device->CreateTexture2D(&target,nullptr,&texture);
            if(FAILED(hr)) return hr;
        }
        textures_=std::move(replacement); device_=device; sourceDesc_=desc;
    }
    ComPtr<ID3D11DeviceContext> context; device->GetImmediateContext(&context);
    if(desc.SampleDesc.Count>1) context->ResolveSubresource(textures_[eye].Get(),0,source,0,desc.Format);
    else context->CopyResource(textures_[eye].Get(),source);
    sequence_=sequence; mask_|=1u<<eye;
    return S_OK;
}
