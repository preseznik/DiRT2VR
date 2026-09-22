#pragma once
#include <windows.h>
#include <string>
// Changes only the calling executable's import, never Windows' global Documents folder.
bool InstallLanDocumentsRedirect(HMODULE host, const std::wstring& documents);
