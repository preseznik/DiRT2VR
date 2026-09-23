#pragma once
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <limits>
#include <vector>

namespace vr {
// A checksum sees original bytes only for changes we made that are still intact.
// Unknown changes, including changes within our detours, remain visible.
struct CodePatch {
    uintptr_t address{};
    std::vector<unsigned char> before, after;
};
inline bool OriginalCodeView(const void* data,size_t bytes,const std::vector<CodePatch>& patches,
                             std::vector<unsigned>& copy) {
    const auto begin=reinterpret_cast<uintptr_t>(data);
    if(bytes%4 || bytes>std::numeric_limits<uintptr_t>::max()-begin) return false;
    const auto end=begin+bytes;
    bool changed=false;
    for(const auto& patch:patches) {
        if(patch.before.empty() || patch.before.size()!=patch.after.size() ||
           patch.after.size()>std::numeric_limits<uintptr_t>::max()-patch.address) continue;
        const auto patchEnd=patch.address+patch.after.size();
        const auto first=std::max(begin,patch.address), last=std::min(end,patchEnd);
        if(first>=last) continue;
        if(std::memcmp(reinterpret_cast<const void*>(patch.address),patch.after.data(),patch.after.size())) continue;
        if(!changed) {
            copy.resize(bytes/4);
            std::memcpy(copy.data(),data,bytes);
            changed=true;
        }
        std::memcpy(reinterpret_cast<unsigned char*>(copy.data())+(first-begin),
                    patch.before.data()+(first-patch.address),last-first);
    }
    return changed;
}
}
