#include <winsock2.h>
#include <windows.h>
#include "diagnostics.h"
#include <filesystem>
#include <fstream>
#include <string>
#include <cstdio>
#include <thread>
#include <intrin.h>

__declspec(noinline) void LogCallerStack() {
    DiRT2VRLanLogClose(43, _ReturnAddress(), _AddressOfReturnAddress());
}

void Check(bool ok, const char* message) {
    if (!ok) { std::fprintf(stderr, "FAIL: %s\n", message); ExitProcess(10); }
}
std::string Read(const std::filesystem::path& path) {
    std::ifstream stream(path); return {std::istreambuf_iterator<char>(stream), {}};
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    auto root = std::filesystem::absolute(argv[1]) / std::to_wstring(GetCurrentProcessId());
    auto log = root / "lan-network.log", previous = root / "lan-network.previous.log";
    std::filesystem::create_directories(root);
    SetEnvironmentVariableW(L"DIRT2VR_OUTPUT", root.c_str());
    SetEnvironmentVariableW(L"DIRT2VR_LOGGING", L"0");
    DiRT2VRLanLogStart(); DiRT2VRLanLog(4, 0, "XSocketClose", "disabled"); DiRT2VRLanLogStop();
    Check(!std::filesystem::exists(log), "default-off produces no log");
    SetEnvironmentVariableW(L"DIRT2VR_LOGGING", L"1");
    WSASetLastError(WSAEWOULDBLOCK);
    DiRT2VRLanLogStart();
    Check(WSAGetLastError() == WSAEWOULDBLOCK, "start preserves Winsock error");
    DiRT2VRLanLog(16, 10060, "XSocketRecv", "socket=%08x", 42);
    Check(WSAGetLastError() == WSAEWOULDBLOCK, "write preserves Winsock error");
    const char packet[] = "private test payload";
    const auto caller = reinterpret_cast<const void*>(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)) + 0x1234);
    DiRT2VRLanLogPacket("XSocketRecvFrom", 42, packet, sizeof(packet) - 1, caller);
    DiRT2VRLanLogClose(42, caller);
    LogCallerStack();
    DiRT2VRLanLogClose(44, caller, reinterpret_cast<const void*>(1));
    Check(WSAGetLastError() == WSAEWOULDBLOCK, "packet and stack diagnostics preserve Winsock error");
    DiRT2VRLanLog(1, 0, "XSocketRecv", "excluded trace");
    DiRT2VRLanLog(4, 0, "XUserAnything", "excluded user data");
    DiRT2VRLanLog(16, 87, "XSessionStart", "invalid start flags");
    DiRT2VRLanLog(4, 0, "XSessionAnything", "excluded session properties");
    DiRT2VRLanLogStop();
    Check(WSAGetLastError() == WSAEWOULDBLOCK, "stop preserves Winsock error");
    auto contents = Read(log);
    Check(contents.find("error=10060 XSocketRecv: socket=0000002a") != std::string::npos, "socket and error metadata recorded");
    Check(contents.find("excluded") == std::string::npos, "trace and unrelated APIs excluded");
    Check(contents.find("error=87 XSessionStart: invalid start flags") != std::string::npos, "session errors included");
    Check(contents.find("close code candidate socket=2b stack_offset=0") != std::string::npos, "caller return slot recorded despite omitted frame pointers");
    Check(contents.find("close code candidate socket=2c") == std::string::npos, "non-stack input ignored");
    Check(contents.find(packet) == std::string::npos && contents.find("checksum=") != std::string::npos && contents.find("caller_rva=1234") != std::string::npos, "packet checksum and caller recorded without payload");
    Check(contents.find("utc_ms=") != std::string::npos && contents.find("tick_ms=") != std::string::npos, "correlation timestamps recorded");
    DiRT2VRLanLogStart();
    auto write = [] { for (int i = 0; i < 300; ++i) DiRT2VRLanLog(4, 0, "ParseNetworkData", "rotation sample %d", i); };
    std::thread first(write), second(write); first.join(); second.join();
    DiRT2VRLanLog(4, 0, "XSocketClose", "final marker");
    DiRT2VRLanLogStop();
    Check(std::filesystem::file_size(log) <= 8192 && std::filesystem::file_size(previous) <= 8192, "rotation bounded to two files");
    Check(Read(log).find("final marker") != std::string::npos, "rotation retains latest evidence");
    auto size = std::filesystem::file_size(log);
    DiRT2VRLanLog(4, 0, "XSocketClose", "after stop");
    Check(std::filesystem::file_size(log) == size, "no writes after stop");
    SetEnvironmentVariableW(L"DIRT2VR_OUTPUT", L"relative-path");
    DiRT2VRLanLogStart(); Check(!DiRT2VRLanLogEnabled(), "relative output rejected");
    std::puts("LAN logging opt-in, filtering, error preservation and bounded rotation passed.");
}
