#include "profile.h"
#include <shlobj.h>
#include <filesystem>
#include <iostream>
#include <cstring>
// Reload the import on each call, as separate game functions do after the entry-point hook.
__declspec(noinline) HRESULT ReadFolder(int folder, char* output) {
    return SHGetFolderPathA(nullptr,folder,nullptr,0,output);
}
int main() {
    char before[MAX_PATH]{},after[MAX_PATH]{},appData[MAX_PATH]{},appDataAfter[MAX_PATH]{};
    if (FAILED(ReadFolder(CSIDL_PERSONAL,before)) || FAILED(ReadFolder(CSIDL_LOCAL_APPDATA,appData))) return 1;
    auto folder=std::filesystem::current_path()/L"isolated-documents-test";
    std::filesystem::create_directories(folder);
    auto host=GetModuleHandleW(nullptr);
    if (!ConfigureLanDocuments(host,true,L"")) return 8;
    if (FAILED(ReadFolder(CSIDL_PERSONAL,after)) || strcmp(before,after)) return 9;
    if (InstallLanDocumentsRedirect(host,L"relative") || InstallLanDocumentsRedirect(host,folder.wstring()+L"\\missing")) return 2;
    if (!ConfigureLanDocuments(host,false,folder.wstring())) return 3;
    if (FAILED(ReadFolder(CSIDL_PERSONAL|CSIDL_FLAG_CREATE,after)) || folder!=std::filesystem::path(after)) return 4;
    if (FAILED(ReadFolder(CSIDL_LOCAL_APPDATA,appDataAfter)) || strcmp(appData,appDataAfter)) return 5;
    using FolderPath=HRESULT(WINAPI*)(HWND,int,HANDLE,DWORD,LPSTR);
    auto system=reinterpret_cast<FolderPath>(GetProcAddress(GetModuleHandleW(L"shell32.dll"),"SHGetFolderPathA"));
    if (FAILED(system(nullptr,CSIDL_PERSONAL,nullptr,0,after)) || strcmp(before,after)) return 6;
    if (InstallLanDocumentsRedirect(host,folder.wstring())) return 7;
    std::cout << "Shared career keeps normal Documents; isolated mode still redirects; other folders unchanged; invalid paths and repeated installation rejected.\n";
}
