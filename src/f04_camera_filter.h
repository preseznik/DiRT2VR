#pragma once
#include <array>
#include <algorithm>
#include <cmath>

class F04CameraFilter {
    bool ready=false;
    float pitch=0,roll=0;
    static float Wrap(float a) { while(a>3.14159265f)a-=6.2831853f;while(a<-3.14159265f)a+=6.2831853f;return a; }
public:
    void Reset() { ready=false; }
    bool Apply(const std::array<float,28>& in,std::array<float,28>& out,float dt) {
        out=in;
        if(!std::isfinite(dt)||dt<=0||dt>.1f){Reset();return false;}
        for(float v:in)if(!std::isfinite(v)){Reset();return false;}
        float length=std::sqrt(in[12]*in[12]+in[13]*in[13]+in[14]*in[14]);
        if(std::abs(length-1)>.01f || std::hypot(in[12],in[14])<.2f){Reset();return false;}
        float yaw=std::atan2(in[12],in[14]);
        float rawPitch=std::atan2(in[13],std::hypot(in[12],in[14]));
        float sy=std::sin(yaw),cy=std::cos(yaw),sp=std::sin(rawPitch),cp=std::cos(rawPitch);
        std::array<float,3> right{cy,0,-sy},up{-sy*sp,cp,-cy*sp};
        float ur=0,uu=0,rr=0;
        for(int i=0;i<3;i++){ur+=in[4+i]*right[i];uu+=in[4+i]*up[i];rr+=in[8+i]*right[i];}
        if(rr<.8f || std::hypot(ur,uu)<.9f){Reset();return false;}
        float rawRoll=std::atan2(ur,uu);
        // Reset on camera cuts/rollovers. Retain exact native yaw and eye position.
        if(!ready || std::abs(rawPitch-pitch)>.35f || std::abs(Wrap(rawRoll-roll))>.35f){pitch=rawPitch;roll=rawRoll;ready=true;}
        float a=1-std::exp(-dt/.18f);
        pitch+=a*(rawPitch-pitch);roll+=a*Wrap(rawRoll-roll);
        constexpr float cap=.10471976f; // At most six degrees behind native pose.
        pitch=std::clamp(pitch,rawPitch-cap,rawPitch+cap);
        roll=rawRoll+std::clamp(Wrap(roll-rawRoll),-cap,cap);
        sp=std::sin(pitch);cp=std::cos(pitch);
        up={-sy*sp,cp,-cy*sp};
        float sr=std::sin(roll),cr=std::cos(roll);
        for(int i=0;i<3;i++){out[4+i]=up[i]*cr+right[i]*sr;out[8+i]=right[i]*cr-up[i]*sr;}
        out[12]=sy*cp;out[13]=sp;out[14]=cy*cp;
        return true;
    }
};
