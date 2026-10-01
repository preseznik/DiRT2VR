#pragma once
#include "xr_frames.h"
#include <string>

namespace vr {
// Explicit local diagnostic sessions only; no input hooks or network service.
bool TestMessagesEnabled();
uint64_t TestMessage(const std::wstring& title,const std::wstring& body,unsigned seconds=120);
bool TestMessageCurrent(uint64_t revision);
bool TestMessageVisibleFor(uint64_t revision,uint64_t milliseconds);
void PollTestMessage();
XrFrames::Overlay* TestMessageOverlay();
void SetTestMessageReference(const XrPosef& reference);
void PrepareTestMessage(bool seatPanelActive);
void TestMessageSubmitted(bool submitted);
}
