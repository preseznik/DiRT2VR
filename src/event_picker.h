#pragma once
#include <d3d11.h>

namespace vr {
// "Next event" row of the Direct pause and finish menus: the flow stays on the
// row's check state while the panel is open (as Photo Mode does), then leaves
// through Return to menus ("enabled") or goes back to the menu ("disabled").
bool EventPickerMenu(void* state,void* result);
bool EventPickerOpen();
// Seat panel navigation bits: 7 up, 8 down, 9 left, 10 right, 11 select, 12 cancel.
void EventPickerInput(unsigned held,unsigned pressed,double dt);
void EventPickerDraw(ID3D11RenderTargetView* target,unsigned w,unsigned h,float x=0,float y=0);
}
