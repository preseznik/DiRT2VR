#pragma once
namespace vr {
bool ReplayStorageRequested();
bool EnableReplayStorage();
void ReplayStorageBegin();
bool ReplayStorageComplete(unsigned char* inner);
void ReplayStorageUpdate(void(__thiscall* original)(void*, const double*), void* root, const double* dt);
}
