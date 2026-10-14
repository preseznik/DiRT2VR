#pragma once
#include <d3d11.h>

namespace vr {
inline bool ValidAnisotropy(unsigned value) {
    return value==0 || value==1 || value==2 || value==4 || value==8 || value==16;
}
inline bool AdjustAnisotropy(D3D11_SAMPLER_DESC& desc,unsigned value) {
    // Only ordinary mipmapped, repeating surface samplers. Leave point sampling,
    // comparison/shadow samplers and clamped screen-space passes alone.
    if(!value || !ValidAnisotropy(value) || desc.MaxLOD<=desc.MinLOD ||
       (desc.Filter!=D3D11_FILTER_MIN_MAG_MIP_LINEAR && desc.Filter!=D3D11_FILTER_ANISOTROPIC)) return false;
    const auto repeat=[](D3D11_TEXTURE_ADDRESS_MODE mode) { return mode==D3D11_TEXTURE_ADDRESS_WRAP || mode==D3D11_TEXTURE_ADDRESS_MIRROR; };
    if(!repeat(desc.AddressU) && !repeat(desc.AddressV)) return false;
    const auto filter=value==1 ? D3D11_FILTER_MIN_MAG_MIP_LINEAR : D3D11_FILTER_ANISOTROPIC;
    if(desc.Filter==filter && desc.MaxAnisotropy==value) return false;
    desc.Filter=filter; desc.MaxAnisotropy=value;
    return true;
}
bool EnableAnisotropicFiltering(ID3D11Device* device);
}
