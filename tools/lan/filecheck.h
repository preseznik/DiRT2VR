#pragma once
#include <windows.h>

// Race-start file check (ProJict 72e4ce74, 8ac0f636): judges the game files
// only, ignoring the code-integrity flag DiRT2VR's hooks trip, and logs each
// checked file's name, expected and local CRC32 when logging is on. Only call
// before the supported game's entry point, after its imports/relocations resolve.
bool InstallLanFileCheck(HMODULE host);
