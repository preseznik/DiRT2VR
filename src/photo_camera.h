#pragma once
#include <algorithm>
#include <array>
#include <cmath>

namespace vr {
struct PhotoInput {
    float right{},forward{},up{},yaw{},pitch{};
    bool fast{},exit{};
};

// EGO records contain up, left, forward and position as padded float4 rows.
// Keep all padding and projection metadata from the engine's external view.
class PhotoCamera {
public:
    bool Begin(const float* record) {
        if(!record)return false;
        for(unsigned i:{4u,5u,6u,8u,9u,10u,12u,13u,14u,16u,17u,18u,20u,21u,22u})
            if(!std::isfinite(record[i]))return false;
        if(record[20]<=0 || record[20]>=3.14f || record[21]<=0 || record[22]<=record[21])return false;
        const float length=std::hypot(record[12],record[13],record[14]);
        if(length<.99f || length>1.01f)return false;
        std::copy_n(record,28,record_.begin());
        pitch_=std::asin(std::clamp(record[13]/length,-1.f,1.f));
        const float cross[3]={record[5]*record[14]-record[6]*record[13],
            record[6]*record[12]-record[4]*record[14],record[4]*record[13]-record[5]*record[12]};
        const float side=-(cross[0]*record[8]+cross[1]*record[9]+cross[2]*record[10]);
        if(std::abs(side)<.9f || std::abs(side)>1.1f)return false;
        handedness_=side>0?1.f:-1.f;
        return ready_=true;
    }
    void Move(PhotoInput input,float seconds) {
        if(!ready_ || !std::isfinite(seconds) || seconds<=0 || seconds>.1f)return;
        for(float v:{input.right,input.forward,input.up,input.yaw,input.pitch})if(!std::isfinite(v))return;
        // Rotate the actual game basis. Reconstructing it from yaw/pitch can
        // reverse its handedness (and cull the world) or discard camera roll.
        if(input.yaw!=0)Rotate({0,1,0},input.yaw*handedness_);
        const float pitch=std::clamp(pitch_+input.pitch,-1.553343f,1.553343f);
        if(pitch!=pitch_)Rotate({-record_[8],-record_[9],-record_[10]},-(pitch-pitch_)*handedness_);
        if(input.yaw!=0 || pitch!=pitch_)Orthonormalize();
        pitch_=pitch;
        const float length=std::max(1.f,std::hypot(input.right,input.forward,input.up));
        const float distance=(input.fast?40.f:8.f)*seconds/length;
        for(unsigned i=0;i<3;++i) {
            record_[16+i]+=distance*(-record_[8+i]*input.right+record_[12+i]*input.forward+(i==1?input.up:0));
        }
    }
    bool Ready() const {return ready_;}
    const std::array<float,28>& Record() const {return record_;}
    void Reset(){ready_=false;}
private:
    static void Normalize(std::array<float,3>& v) {
        const float length=std::hypot(v[0],v[1],v[2]);
        for(auto& value:v)value/=length;
    }
    void Orthonormalize() {
        std::array<float,3> forward{record_[12],record_[13],record_[14]},right{-record_[8],-record_[9],-record_[10]};
        Normalize(forward);
        const float dot=forward[0]*right[0]+forward[1]*right[1]+forward[2]*right[2];
        for(unsigned i=0;i<3;++i)right[i]-=forward[i]*dot;
        Normalize(right);
        const float up[3]={forward[1]*right[2]-forward[2]*right[1],
            forward[2]*right[0]-forward[0]*right[2],forward[0]*right[1]-forward[1]*right[0]};
        for(unsigned i=0;i<3;++i) {
            record_[4+i]=up[i]*handedness_;record_[8+i]=-right[i];record_[12+i]=forward[i];
        }
    }
    void Rotate(std::array<float,3> axis,float angle) {
        Normalize(axis);
        const float s=std::sin(angle),c=std::cos(angle);
        for(unsigned row:{4u,8u,12u}) {
            std::array<float,3> v{record_[row],record_[row+1],record_[row+2]};
            const float dot=axis[0]*v[0]+axis[1]*v[1]+axis[2]*v[2];
            const float cross[3]={axis[1]*v[2]-axis[2]*v[1],axis[2]*v[0]-axis[0]*v[2],axis[0]*v[1]-axis[1]*v[0]};
            for(unsigned i=0;i<3;++i)record_[row+i]=v[i]*c+cross[i]*s+axis[i]*dot*(1-c);
        }
    }
    std::array<float,28> record_{};
    float pitch_{},handedness_{1};
    bool ready_{};
};

// The selected native entry stores quaternion/position rather than the bridge's
// exported matrix. Match 0x7293c0's conversion without its frozen-view branches.
inline bool SeedPhotoCamera(std::array<float,28>& record,const std::array<float,14>& pose) {
    for(unsigned i:{0u,1u,2u,3u,4u,5u,6u,12u,13u})if(!std::isfinite(pose[i]))return false;
    const float x=pose[0],y=pose[1],z=pose[2],w=pose[3];
    const float norm=x*x+y*y+z*z+w*w;
    if(std::abs(norm-1.f)>.01f)return false;
    auto next=record;
    // Native left=Q(+X), up=Q(+Y), forward=Q(+Z).
    const float up[]={2*(x*y-z*w),1-2*(x*x+z*z),2*(y*z+x*w)};
    const float left[]={1-2*(y*y+z*z),2*(x*y+z*w),2*(x*z-y*w)};
    const float forward[]={2*(x*z+y*w),2*(y*z-x*w),1-2*(x*x+y*y)};
    for(unsigned i=0;i<3;++i) {
        next[4+i]=up[i];next[8+i]=left[i];next[12+i]=forward[i];next[16+i]=pose[4+i];
    }
    next[20]=pose[12];next[21]=pose[13];
    PhotoCamera validation;
    if(!validation.Begin(next.data()))return false;
    record=next;return true;
}
}
