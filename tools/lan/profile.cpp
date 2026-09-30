#include "profile.h"
#include <shlobj.h>
#include <cstring>
#include <cstdint>
#include "SaveRoutePath.h"

namespace {
char careerRoot[MAX_PATH]{};
bool InstallSaveMount(HMODULE host, const wchar_t* path) {
    if(!profile_route::ResolvePath(path,careerRoot)) return false;
    auto base=reinterpret_cast<std::uint8_t*>(host);
    // mov ecx,[esp+53c] supplies the physical root to the /saves/ mount only.
    const std::uint8_t expected[]{0x8b,0x8c,0x24,0x3c,0x05,0x00,0x00};
    if(memcmp(base+0x2ae327,expected,sizeof(expected)) ||
       strcmp(reinterpret_cast<char*>(base+0xf22cb0),"/saves/")) return false;
    std::uint8_t patch[]{0xb9,0,0,0,0,0x90,0x90};
    const auto pointer=reinterpret_cast<std::uint32_t>(careerRoot);
    memcpy(patch+1,&pointer,4);
    DWORD old{},ignored{};
    if(!VirtualProtect(base+0x2ae327,sizeof(patch),PAGE_EXECUTE_READWRITE,&old)) return false;
    memcpy(base+0x2ae327,patch,sizeof(patch));
    return VirtualProtect(base+0x2ae327,sizeof(patch),old,&ignored) &&
           FlushInstructionCache(GetCurrentProcess(),base+0x2ae327,sizeof(patch));
}
}


namespace {
using FolderPath = HRESULT(WINAPI*)(HWND,int,HANDLE,DWORD,LPSTR);
FolderPath original{};
char redirected[MAX_PATH]{};
HRESULT WINAPI GetFolder(HWND window, int folder, HANDLE token, DWORD flags, LPSTR output) {
    if ((folder & 0xff) != CSIDL_PERSONAL) return original(window,folder,token,flags,output);
    if (!output) return E_INVALIDARG;
    strcpy_s(output,MAX_PATH,redirected);
    return S_OK;
}
}

bool ConfigureLanDocuments(HMODULE host, bool sharedCareer, const std::wstring& documents) {
    if (!sharedCareer && !InstallLanDocumentsRedirect(host,documents)) return false;
    wchar_t path[32768]{};
    const auto length=GetEnvironmentVariableW(L"DIRT2VR_PROFILE_ROOT",path,32768);
    if (!length) return true;
    return length<32768 && InstallSaveMount(host,path);
}

bool InstallLanDocumentsRedirect(HMODULE host, const std::wstring& documents) {
    if (original || documents.size()<3 || documents.size()>=MAX_PATH || documents[1]!=L':' || documents[2]!=L'\\') return false;
    const auto attrs=GetFileAttributesW(documents.c_str());
    if (attrs==INVALID_FILE_ATTRIBUTES || !(attrs&FILE_ATTRIBUTE_DIRECTORY) || (attrs&FILE_ATTRIBUTE_REPARSE_POINT)) return false;
    BOOL substituted=FALSE;
    if (!WideCharToMultiByte(CP_ACP,WC_NO_BEST_FIT_CHARS,documents.c_str(),-1,redirected,MAX_PATH,nullptr,&substituted) || substituted) return false;
    auto base=reinterpret_cast<unsigned char*>(host);
    const auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (!base || dos->e_magic!=IMAGE_DOS_SIGNATURE) return false;
    const auto nt=reinterpret_cast<IMAGE_NT_HEADERS*>(base+dos->e_lfanew);
    if (nt->Signature!=IMAGE_NT_SIGNATURE) return false;
    const auto imports=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!imports.VirtualAddress) return false;
    auto descriptor=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base+imports.VirtualAddress);
    for (;descriptor->Name;++descriptor) {
        if (_stricmp(reinterpret_cast<char*>(base+descriptor->Name),"shell32.dll") || !descriptor->OriginalFirstThunk) continue;
        auto names=reinterpret_cast<IMAGE_THUNK_DATA*>(base+descriptor->OriginalFirstThunk);
        auto slots=reinterpret_cast<IMAGE_THUNK_DATA*>(base+descriptor->FirstThunk);
        for (;names->u1.AddressOfData;++names,++slots) {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            const auto name=reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base+names->u1.AddressOfData);
            if (strcmp(reinterpret_cast<char*>(name->Name),"SHGetFolderPathA")) continue;
            DWORD protection{};
            if (!VirtualProtect(&slots->u1.Function,sizeof(slots->u1.Function),PAGE_READWRITE,&protection)) return false;
            original=reinterpret_cast<FolderPath>(slots->u1.Function);
            slots->u1.Function=reinterpret_cast<ULONG_PTR>(&GetFolder);
            DWORD ignored{};
            return VirtualProtect(&slots->u1.Function,sizeof(slots->u1.Function),protection,&ignored)!=FALSE;
        }
    }
    return false;
}
