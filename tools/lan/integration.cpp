#include "profile.h"
#include "intro.h"
#include "common.h"
#include <fstream>
#include <filesystem>

// Called by our pinned XLLN entry-point integration, after imports resolve and before game code.
void DiRT2VRLanInitialize() {
    wchar_t documents[MAX_PATH]{};
    const auto size=GetEnvironmentVariableW(L"DIRT2VR_LAN_DOCUMENTS",documents,MAX_PATH);
    if (!vr::SupportedHost() || !size || size>=MAX_PATH || !InstallLanDocumentsRedirect(GetModuleHandleW(nullptr),documents)) {
        MessageBoxW(nullptr,L"LAN profile isolation failed. The game will close without starting. Run the LAN test script with the supported game and a local profile path.",L"DiRT2VR LAN test",MB_ICONERROR);
        ExitProcess(ERROR_INVALID_DATA);
    }
    wchar_t skip[4]{};
    const bool skipIntro=GetEnvironmentVariableW(L"DIRT2VR_LAN_SKIP_INTRO",skip,4)==1 && skip[0]==L'1';
    if (skipIntro && !InstallLanIntroSkip(GetModuleHandleW(nullptr))) {
        MessageBoxW(nullptr,L"The introduction bypass did not match the supported game. The game will close; disable Skip introduction in LAN settings to use normal onboarding.",L"DiRT2VR LAN test",MB_ICONERROR);
        ExitProcess(ERROR_INVALID_DATA);
    }
    // One overwritten readiness receipt, not a diagnostic log.
    std::ofstream receipt(std::filesystem::path(documents)/L"profile-ready.txt",std::ios::trunc);
    receipt << "Documents import redirected before game entry\n";
    receipt << "Introduction bypass: " << (skipIntro ? "enabled" : "disabled") << '\n';
    receipt.flush();
    if (!receipt) ExitProcess(ERROR_WRITE_FAULT);
}
