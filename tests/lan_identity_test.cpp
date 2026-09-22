// Exercise the packaged DLL's address -> machine identity contract, without launching a game.
#include <winsock2.h>
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <set>
#include <fstream>
struct Address { IN_ADDR local, online; uint16_t port; uint8_t ethernet[6], onlineData[20]; };
static_assert(sizeof(Address) == 36);
void Check(bool ok, const char* message) {
    if (!ok) { std::fprintf(stderr, "FAIL: %s (error=%lu)\n", message, GetLastError()); ExitProcess(10); }
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    wchar_t executable[MAX_PATH]{};
    const DWORD length = GetModuleFileNameW(nullptr, executable, MAX_PATH);
    Check(length && length < MAX_PATH, "resolve test executable path");
    const auto config = std::filesystem::path(executable).parent_path() / "identity-fixture/xlln.ini";
    std::filesystem::create_directories(config.parent_path());
    std::ofstream(config) << "[XLLN-Config-Version:1.6.2.1]\nxlive_net_disable = 1\nxlln_debug_log_level = 0x00000000\n";
    SetEnvironmentVariableW(L"DIRT2VR_LAN_CONFIG", config.c_str());
    SetEnvironmentVariableW(L"DIRT2VR_LAN_DISCOVERY", L"0");
    auto dll = LoadLibraryW(std::filesystem::absolute(argv[1]).c_str());
    Check(dll != nullptr, "load DLL");
    auto addressFor = reinterpret_cast<int(WINAPI*)(IN_ADDR, Address*, void*)>(GetProcAddress(dll, "XNetInAddrToXnAddr"));
    auto machineFor = reinterpret_cast<int(WINAPI*)(const Address*, uint64_t*)>(GetProcAddress(dll, "XNetXnAddrToMachineId"));
    auto reverse = reinterpret_cast<int(WINAPI*)(Address*, void*, IN_ADDR*)>(GetProcAddress(dll, "XNetXnAddrToInAddr"));
    Check(addressFor && machineFor && reverse, "identity exports");
    std::set<uint64_t> identities;
    for (uint32_t instance : {0x7774e2u, 0x2e5350u, 0x17774e2u}) {
        IN_ADDR input{}; input.s_addr = htonl(instance);
        Address address{}; uint64_t identity = UINT64_MAX, repeated = UINT64_MAX;
        Check(addressFor(input, &address, nullptr) == 0, "resolve address");
        Check(machineFor(&address, &identity) == 0, "resolve machine identity");
        std::printf("instance=%08x machine=%016llx\n", instance, identity);
        std::fflush(stdout);
        Check(identity != 0, "generated machine identity is not the null identity");
        Check(identities.insert(identity).second, "distinct peers have distinct machine identities");
        Address again{}; addressFor(input, &again, nullptr); machineFor(&again, &repeated);
        Check(repeated == identity, "same peer has stable identity");
        IN_ADDR back{};
        Check(reverse(&address, nullptr, &back) == 0 && back.s_addr == input.s_addr, "routing address round trip remains unchanged");
    }
    uint64_t identity = 0;
    Check(machineFor(nullptr, &identity) != 0, "null address rejected");
    FreeLibrary(dll);
    std::puts("LAN peer identity checks passed.");
}
