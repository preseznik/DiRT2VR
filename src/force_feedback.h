#pragma once
namespace vr {
bool ForceFeedbackRequested();
bool EnableForceFeedback();
// Live driving started (flashback recorder); turns the player's FFB on if it is off.
void ForceFeedbackLiveDriving();
}
