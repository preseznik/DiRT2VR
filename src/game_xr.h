#pragma once
#include "xr_frames.h"
#include "eye_blit.h"
#include <memory>

class GameXr {
public:
    ~GameXr() { Shutdown(); }
    bool Initialize(ID3D11Device* device,float scale);
    bool Tick(const XrFrames::Draw& draw,const XrFrames::Prepare& prepare={});
    bool CopyEye(unsigned eye,ID3D11Texture2D* image,ID3D11RenderTargetView* target,unsigned w,unsigned h);
    void Shutdown();
    bool Active() const { return frames_!=nullptr; }
    bool Exiting() const { return frames_ && frames_->Exiting(); }
    bool Visible() const { return frames_ && frames_->Visible(); }
    uint64_t Submitted() const { return frames_ ? frames_->Submitted() : 0; }
private:
    XrInstance instance_{};
    XrSession session_{};
    std::unique_ptr<XrFrames> frames_;
    EyeBlit blit_;
};
