#include "profile.h"
#include "intro.h"
#include "common.h"
#include <fstream>
#include <filesystem>

// Called by our pinned XLLN entry-point integration, after imports resolve and before game code.
void DiRT2VRLanInitialize() {
    wchar_t shared[4]{};
    const bool sharedCareer=GetEnvironmentVariableW(L"DIRT2VR_LAN_SHARED_CAREER",shared,4)==1 && shared[0]==L'1';
    wchar_t documents[MAX_PATH]{};
    const auto size=sharedCareer ? 0 : GetEnvironmentVariableW(L"DIRT2VR_LAN_DOCUMENTS",documents,MAX_PATH);
    if (!vr::SupportedHost() || (!sharedCareer && (!size || size>=MAX_PATH)) || !ConfigureLanDocuments(GetModuleHandleW(nullptr),sharedCareer,documents)) {
        MessageBoxW(nullptr,L"LAN startup did not match the supported game or profile configuration. Launch through DiRT2VR.",L"DiRT2VR LAN",MB_ICONERROR);
        ExitProcess(ERROR_INVALID_DATA);
    }
    wchar_t skip[4]{};
    const bool skipIntro=GetEnvironmentVariableW(L"DIRT2VR_LAN_SKIP_INTRO",skip,4)==1 && skip[0]==L'1';
    if (skipIntro && !InstallLanIntroSkip(GetModuleHandleW(nullptr))) {
        MessageBoxW(nullptr,L"The introduction bypass did not match the supported game. The game will close; disable Skip introduction in LAN settings to use normal onboarding.",L"DiRT2VR LAN test",MB_ICONERROR);
        ExitProcess(ERROR_INVALID_DATA);
    }
    // One overwritten readiness receipt, not a diagnostic log.
    std::filesystem::path receiptPath=std::filesystem::path(documents)/L"profile-ready.txt";
    if (sharedCareer) {
        wchar_t target[32768]{};
        const auto length=GetEnvironmentVariableW(L"DIRT2VR_LAN_RECEIPT",target,32768);
        if (!length || length>=32768 || !std::filesystem::path(target).is_absolute()) ExitProcess(ERROR_INVALID_DATA);
        receiptPath=target;
    }
    std::ofstream receipt(receiptPath,std::ios::trunc);
    receipt << (sharedCareer ? "Normal career: system Documents lookup unchanged\n" : "Documents import redirected before game entry\n");
    receipt << "Introduction bypass: " << (skipIntro ? "enabled" : "disabled") << '\n';
    receipt.flush();
    if (!receipt) ExitProcess(ERROR_WRITE_FAULT);
}
