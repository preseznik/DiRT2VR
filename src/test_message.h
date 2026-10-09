#pragma once
#include "xr_frames.h"
#include <string>

namespace vr {
// Explicit local diagnostic sessions only. Replies stay in the session log
// directory; there is no network service or background keyboard recording.
bool TestMessagesEnabled();
bool EnableTestMessageInput();
bool TestMessageTyping();
bool TestMessageBlocksKey(unsigned key);
bool TestMessageWindowKey(UINT message,WPARAM key,LPARAM flags);
uint64_t TestMessage(const std::wstring& title,const std::wstring& body,unsigned seconds=120);
bool TestMessageCurrent(uint64_t revision);
bool TestMessageVisibleFor(uint64_t revision,uint64_t milliseconds);
void PollTestMessage();
XrFrames::Overlay* TestMessageOverlay();
void SetTestMessageReference(const XrPosef& reference);
void PrepareTestMessage(bool seatPanelActive);
void TestMessageSubmitted(bool submitted);
}
