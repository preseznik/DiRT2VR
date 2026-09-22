#pragma once
#include <windows.h>

// Only call before the supported game's entry point, after its imports/relocations resolve.
bool InstallLanIntroSkip(HMODULE host);
