#pragma once
#include <windows.h>
#include <string>
// Changes only the calling executable's import, never Windows' global Documents folder.
bool InstallLanDocumentsRedirect(HMODULE host, const std::wstring& documents);
// Shared mode keeps the game's own Documents lookup and single normal career unchanged.
bool ConfigureLanDocuments(HMODULE host, bool sharedCareer, const std::wstring& documents);
