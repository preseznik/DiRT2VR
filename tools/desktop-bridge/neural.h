#pragma once
#include "../../src/desktop_bridge_protocol.h"
#include <cstdio>
#include <filesystem>

// Serves frames through DLSS 5 Neural Rendering until stopped. The runtime folder
// holds the player's nvngx_dlssnr.dll; the caller shim sits next to this helper.
int RunNeural(vr::bridge::Shared& shared,HANDLE request,HANDLE done,HANDLE stop,const std::filesystem::path& runtime,FILE* log);
