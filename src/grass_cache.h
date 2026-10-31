#pragma once
#include <cstdint>
#include <optional>
#include <vector>

namespace vr::grass {
// The ground-cover instance cache allocator (native 979440/979510/9792a0) keeps
// each block's length in a ushort, so the cache stops at 65,535 clumps. This is
// the same first-fit/merge/compact policy with 32-bit lengths. Blocks always
// cover [0, items) in address order and adjacent free blocks are merged.
class Cache {
public:
    static constexpr uint32_t Free=0xffffffff;
    struct Block { uint32_t start{},length{},owner{Free}; };
    void Reset(uint32_t items) { blocks_.assign(1,Block{0,items,Free}); items_=items; }
    uint32_t Items() const { return items_; }
    const std::vector<Block>& Blocks() const { return blocks_; }
    // Native policy: an exact-size free block first, otherwise the first larger one.
    std::optional<uint32_t> Allocate(uint32_t owner,uint32_t count) {
        if(owner==Free || count>items_) return std::nullopt;
        size_t chosen=blocks_.size();
        for(size_t i=0;i<blocks_.size();++i) if(blocks_[i].owner==Free && blocks_[i].length==count) { chosen=i; break; }
        if(chosen==blocks_.size())
            for(size_t i=0;i<blocks_.size();++i) if(blocks_[i].owner==Free && blocks_[i].length>count) { chosen=i; break; }
        if(chosen==blocks_.size()) return std::nullopt;
        Block& free=blocks_[chosen];
        const uint32_t start=free.start;
        if(free.length==count) { free.owner=owner; return start; }
        free.start+=count; free.length-=count;
        blocks_.insert(blocks_.begin()+static_cast<ptrdiff_t>(chosen),Block{start,count,owner});
        return start;
    }
    bool Release(uint32_t owner) {
        const size_t i=Find(owner);
        if(i==blocks_.size()) return false;
        blocks_[i].owner=Free;
        MergeAround(i);
        return true;
    }
    // Slides allocated blocks down into the lowest free block, at most `steps`
    // times. move(dst,src,count) copies instances; relocated(owner,start,count)
    // reports each block's new place.
    template<class Move,class Relocated> void Compact(uint32_t steps,Move move,Relocated relocated) {
        for(;steps;--steps) {
            size_t f=0;
            while(f<blocks_.size() && blocks_[f].owner!=Free) ++f;
            if(f+1>=blocks_.size()) return;
            Block gap=blocks_[f],moved=blocks_[f+1];
            move(gap.start,moved.start,moved.length);
            blocks_[f]=Block{gap.start,moved.length,moved.owner};
            blocks_[f+1]=Block{gap.start+moved.length,gap.length,Free};
            relocated(moved.owner,gap.start,moved.length);
            MergeAround(f+1);
        }
    }
    uint32_t AllocatedBlocks() const { uint32_t n{}; for(auto& b:blocks_) n+=b.owner!=Free; return n; }
    uint32_t FreeBlocks() const { uint32_t n{}; for(auto& b:blocks_) n+=b.owner==Free; return n; }
    uint32_t UsedItems() const { uint32_t n{}; for(auto& b:blocks_) if(b.owner!=Free) n+=b.length; return n; }
private:
    size_t Find(uint32_t owner) const {
        for(size_t i=0;i<blocks_.size();++i) if(blocks_[i].owner==owner) return i;
        return blocks_.size();
    }
    void MergeAround(size_t i) {
        if(i+1<blocks_.size() && blocks_[i+1].owner==Free) {
            blocks_[i].length+=blocks_[i+1].length;
            blocks_.erase(blocks_.begin()+static_cast<ptrdiff_t>(i)+1);
        }
        if(i>0 && blocks_[i-1].owner==Free) {
            blocks_[i-1].length+=blocks_[i].length;
            blocks_.erase(blocks_.begin()+static_cast<ptrdiff_t>(i));
        }
    }
    std::vector<Block> blocks_;
    uint32_t items_{};
};
}
