// Exercises the real packaged x86 DLL through public XLive APIs and a loopback wire peer.
// LoadLibrary runs after this test's entry point, so the game-specific entry hook is never invoked.
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include <cstring>

namespace {
struct InitInfo { UINT size; DWORD flags; void* d3d; void* pp; WORD lang, reserved; char* adapter; WORD port, reserved2; };
#pragma pack(push, 1)
struct Title { unsigned id = 0xabcdef; unsigned short source = 23074, dest = 23075; };
struct Request { unsigned char type = 17; Title title; };
struct Probe { unsigned char type = 18; Title title; unsigned char response = 4; };
struct Data { unsigned char type = 19; Title title; unsigned short sequence = 0; unsigned size = 0; };
struct Ack { unsigned char type; Title title; unsigned short sequence, count; };
#pragma pack(pop)
static_assert(sizeof(Probe) == 10 && sizeof(Ack) == 13 && sizeof(Data) == 15);
void Check(bool success, const char* message) {
    if (!success) { std::fprintf(stderr, "FAIL: %s (WSA=%d)\n", message, WSAGetLastError()); ExitProcess(10); }
}
template<typename T> T Api(HMODULE dll, const char* name) {
    auto address = GetProcAddress(dll, name);
    Check(address != nullptr, name);
    return reinterpret_cast<T>(address);
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    const auto config = std::filesystem::absolute(argv[2]);
    std::filesystem::create_directories(config.parent_path());
    std::ofstream(config) << "[XLLN-Config-Version:1.6.2.1]\n"
        "xlln_network_instance_base_port = 41090\nxlln_network_instance_port = 41091\n"
        "xlive_net_disable = 0\nxlive_xhv_engine_enabled = 0\nxlln_debug_log_level = 0x00000000\n";
    SetEnvironmentVariableW(L"DIRT2VR_LAN_CONFIG", config.c_str());
    SetEnvironmentVariableW(L"DIRT2VR_LAN_DISCOVERY", L"0");
    const auto library = std::filesystem::absolute(argv[1]);
    auto dll = LoadLibraryW(library.c_str());
    Check(dll != nullptr, "load LAN DLL");
    auto init = Api<HRESULT(WINAPI*)(InitInfo*, unsigned)>(dll, "XLiveInitializeEx");
    auto create = Api<SOCKET(WINAPI*)(int, int, int)>(dll, "XSocketCreate");
    auto bindTitle = Api<int(WINAPI*)(SOCKET, const sockaddr*, int)>(dll, "XSocketBind");
    auto listenTitle = Api<int(WINAPI*)(SOCKET, int)>(dll, "XSocketListen");
    auto acceptTitle = Api<SOCKET(WINAPI*)(SOCKET, sockaddr*, int*)>(dll, "XSocketAccept");
    auto ioctlTitle = Api<int(WINAPI*)(SOCKET, long, u_long*)>(dll, "XSocketIOCTLSocket");
    auto recvTitle = Api<int(WINAPI*)(SOCKET, char*, int, int)>(dll, "XSocketRecv");
    auto getOption = Api<int(WINAPI*)(SOCKET, int, int, char*, int*)>(dll, "XSocketGetSockOpt");
    auto closeTitle = Api<int(WINAPI*)(SOCKET)>(dll, "XSocketClose");
    auto uninit = Api<void(WINAPI*)()>(dll, "XLiveUninitialize");
    InitInfo info{}; info.size = sizeof(info); info.flags = 2; // No auto-logon window.
    Check(SUCCEEDED(init(&info, 0)), "initialize LAN");
    WSADATA wsa{};
    Check(WSAStartup(MAKEWORD(2, 2), &wsa) == 0, "Winsock startup");
    auto listener = create(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    sockaddr_in title{}; title.sin_family = AF_INET; title.sin_port = htons(23075);
    Check(bindTitle(listener, reinterpret_cast<sockaddr*>(&title), sizeof(title)) == 0, "bind title socket");
    Check(listenTitle(listener, 4) == 0, "listen");
    u_long nonblocking = 1;
    Check(ioctlTitle(listener, FIONBIO, &nonblocking) == 0, "nonblocking listener");
    auto peer = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    sockaddr_in loop{}; loop.sin_family = AF_INET; loop.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    Check(bind(peer, reinterpret_cast<sockaddr*>(&loop), sizeof(loop)) == 0, "bind wire peer");
    ioctlsocket(peer, FIONBIO, &nonblocking);
    loop.sin_port = htons(41091);
    auto sendPacket = [&](const void* packet, int size) {
        Check(sendto(peer, static_cast<const char*>(packet), size, 0, reinterpret_cast<sockaddr*>(&loop), sizeof(loop)) == size, "send wire packet");
    };
    Request request;
    sendPacket(&request, sizeof(request));
    SOCKET accepted = INVALID_SOCKET;
    unsigned short serverPort = 0;
    char buffer[1500]; int received;
    auto end = GetTickCount64() + 3000;
    while (GetTickCount64() < end && (accepted == INVALID_SOCKET || !serverPort)) {
        if (accepted == INVALID_SOCKET) accepted = acceptTitle(listener, nullptr, nullptr);
        while ((received = recv(peer, buffer, sizeof(buffer), 0)) > 0) {
            if (received == sizeof(Probe) && buffer[0] == 18 && buffer[9] == 1) {
                Title response; memcpy(&response, buffer + 1, sizeof(response));
                serverPort = response.source;
            }
        }
        Sleep(5);
    }
    Check(accepted != INVALID_SOCKET && serverPort, "connection handshake");
    Check(ioctlTitle(accepted, FIONBIO, &nonblocking) == 0, "nonblocking accepted socket");
    Probe probe; probe.title.dest = serverPort;
    unsigned short sentSequence = 0;
    auto sendData = [&](unsigned short sequence, const std::string& payload) {
        Data data; data.title.dest = serverPort; data.sequence = sequence; data.size = static_cast<unsigned>(payload.size());
        std::vector<char> packet(sizeof(data) + payload.size());
        memcpy(packet.data(), &data, sizeof(data));
        memcpy(packet.data() + sizeof(data), payload.data(), payload.size());
        sendPacket(packet.data(), static_cast<int>(packet.size()));
    };
    int replies = 0;
    bool selectiveAck = false;
    auto drain = [&]() {
        int count = 0;
        while ((received = recv(peer, buffer, sizeof(buffer), 0)) > 0) {
            Check(++count < 100, "no unbounded reply loop");
            if (buffer[0] == 20 && received >= sizeof(Ack)) {
                Ack ack; memcpy(&ack, buffer, sizeof(ack));
                Check(ack.title.dest == 23074 && ack.title.source == serverPort, "ACK ports");
                Check(received == sizeof(Ack) + ack.count * sizeof(unsigned short), "ACK length");
                Check(ack.sequence <= sentSequence, "ACK cannot acknowledge unsent data");
                if (ack.sequence == 1 && ack.count == 1 && buffer[sizeof(Ack)] == 2) selectiveAck = true;
                ++replies;
                // Existing protocol ends ACK retransmission with an empty sequence marker.
                sendData(sentSequence, "");
            }
        }
    };
    // Exercise selective/cumulative acknowledgements while keepalive and application data overlap.
    sentSequence = 3;
    sendData(0, "one"); sendData(2, "three");
    sendPacket(&probe, sizeof(probe));
    end = GetTickCount64() + 700;
    while (GetTickCount64() < end && !selectiveAck) { drain(); Sleep(5); }
    Check(selectiveAck, "selective ACK retains out-of-order packet");
    sendData(1, "two");
    std::string delivered;
    end = GetTickCount64() + 1000;
    while (GetTickCount64() < end && delivered.size() < 11) {
        drain();
        int size = recvTitle(accepted, buffer, sizeof(buffer), 0);
        if (size > 0) delivered.append(buffer, size);
        Sleep(5);
    }
    Check(delivered == "onetwothree", "in-order delivery without duplicates");
    drain();
    const int beforeIdle = replies;
    int probes = 0;
    ULONGLONG next = 0, lastReply = GetTickCount64();
    end = GetTickCount64() + 8000; // Longer than the backend's approximately five-second timeout.
    while (GetTickCount64() < end) {
        if (GetTickCount64() >= next) {
            sendPacket(&probe, sizeof(probe)); ++probes; next = GetTickCount64() + 200;
        }
        int before = replies; drain();
        if (replies != before) lastReply = GetTickCount64();
        Check(GetTickCount64() - lastReply < 1500, "idle peer receives keepalive acknowledgements");
        Sleep(5);
    }
    Check(replies - beforeIdle >= probes - 1 && replies - beforeIdle <= probes + 5, "bounded keepalive replies");
    std::printf("Idle probes=%d replies=%d; data and selective ACK checks passed.\n", probes, replies - beforeIdle);
    // A black-holed peer must still time out: the fix must not merely suppress disconnects.
    closesocket(peer);
    int error = 0;
    end = GetTickCount64() + 8000;
    while (GetTickCount64() < end && !error) {
        int size = sizeof(error);
        Check(getOption(accepted, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&error), &size) == 0, "read socket error");
        Sleep(20);
    }
    Check(error == WSAETIMEDOUT, "unresponsive peer still times out");
    closeTitle(accepted); closeTitle(listener); uninit(); FreeLibrary(dll); WSACleanup();
    std::puts("Disconnected-peer timeout and teardown passed.");
    return 0;
}
