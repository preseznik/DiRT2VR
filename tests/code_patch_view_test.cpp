#include "code_patch_view.h"
#include <array>
#include <cstdio>
#include <stdexcept>

void Check(bool value,const char* message) { if(!value) throw std::runtime_error(message); }
int main() {
    std::array<unsigned char,64> original{},live{};
    for(unsigned i=0;i<original.size();++i) original[i]=static_cast<unsigned char>(i);
    live=original;
    std::vector<vr::CodePatch> patches;
    auto add=[&](size_t offset,size_t size) {
        vr::CodePatch p;
        p.address=reinterpret_cast<uintptr_t>(live.data()+offset);
        p.before.assign(live.begin()+offset,live.begin()+offset+size);
        for(size_t i=offset;i<offset+size;++i) live[i]=0x90;
        p.after.assign(live.begin()+offset,live.begin()+offset+size);
        patches.push_back(p);
    };
    add(7,5); add(30,7); // Entry detour and hot-patch-above, crossing checksum blocks.
    for(size_t start=0;start<64;start+=4) for(size_t size=4;start+size<=64;size+=4) {
        std::vector<unsigned> result;
        bool intersects=(start<12&&start+size>7)||(start<37&&start+size>30);
        Check(vr::OriginalCodeView(live.data()+start,size,patches,result)==intersects,"overlap result");
        if(intersects) Check(!memcmp(result.data(),original.data()+start,size),"known code reconstructed");
    }
    std::vector<unsigned> result;
    live[20]=0xa7;
    Check(vr::OriginalCodeView(live.data(),64,patches,result),"known edits present");
    Check(reinterpret_cast<unsigned char*>(result.data())[20]==0xa7,"unrelated change preserved");
    live[8]=0xcc;
    Check(vr::OriginalCodeView(live.data(),64,patches,result),"second intact patch restored");
    Check(!memcmp(reinterpret_cast<unsigned char*>(result.data())+7,live.data()+7,5),"changed detour stays visible");
    result.clear();
    Check(!vr::OriginalCodeView(live.data()+4,8,{patches[0]},result),"changed full detour rejects partial overlap");
    Check(result.empty(),"no allocation for rejected edit");
    Check(!vr::OriginalCodeView(live.data(),3,patches,result),"non-word length rejected");
    auto malformed=patches[0]; malformed.after.clear();
    Check(!vr::OriginalCodeView(live.data(),64,{malformed},result),"malformed patch ignored");
    Check(!vr::OriginalCodeView(reinterpret_cast<void*>(UINTPTR_MAX-1),4,patches,result),"overflow rejected");
    Check(live[7]==0x90&&live[8]==0xcc&&live[20]==0xa7,"live code never changed");
    puts("Known patches reconstructed; partial blocks, unrelated changes and modified detours checked.");
}
