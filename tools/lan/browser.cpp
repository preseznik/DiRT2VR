#include <winsock2.h>
#include <ws2tcpip.h>
#include <unknwn.h>
#include "xlive/xlive.hpp"
#include "xlive/xsession.hpp"
#include "xlive/xlocator.hpp"
#include "xlln/xlln-network.hpp"
#include <cstring>

namespace {
#ifndef DIRT2VR_DISCOVERY_PORT
#define DIRT2VR_DISCOVERY_PORT 39820
#endif
constexpr unsigned short discoveryPort=DIRT2VR_DISCOVERY_PORT;
bool enabled=false, attempted=false, joining=false, hosting=false;
SOCKET browser=INVALID_SOCKET;
SOCKADDR_STORAGE joinAddress{};
ULONGLONG nextReply=0;
HANDLE stopEvent=nullptr, worker=nullptr;
void PollDiscovery();
DWORD WINAPI DiscoveryWorker(void*) {
    while (WaitForSingleObject(stopEvent,25)==WAIT_TIMEOUT) PollDiscovery();
    return 0;
}
#pragma pack(push,1)
struct Query { char magic[8]; uint64_t nonce; };
struct Reply {
    char magic[8]; uint64_t nonce; uint32_t protocol; uint16_t port; uint8_t state; uint8_t reserved;
    uint32_t players; uint32_t capacity; uint64_t session; char host[64];
};
#pragma pack(pop)
static_assert(sizeof(Reply)==104);
bool LocalAddress(uint32_t address) {
    const auto ip=ntohl(address);
    return (ip>>24)==10 || (ip>>24)==127 || (ip>>20)==0xac1 || (ip>>16)==0xc0a8 || (ip>>16)==0xa9fe;
}
}

// Configuration comes only from the local session manager; LAN packets cannot launch or join.
void DiRT2VRLanBrowserConfigure() {
    char setting[4]{};
    enabled=GetEnvironmentVariableA("DIRT2VR_LAN_DISCOVERY",setting,4)==1 && setting[0]=='1';
    hosting=GetEnvironmentVariableA("DIRT2VR_LAN_HOST",setting,4)==1 && setting[0]=='1';
    if (enabled && !worker) {
        stopEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        if (stopEvent) worker=CreateThread(nullptr,0,DiscoveryWorker,nullptr,0,nullptr);
        if (!worker) ExitProcess(ERROR_NOT_ENOUGH_MEMORY);
    }
    char target[64]{};
    const auto length=GetEnvironmentVariableA("DIRT2VR_LAN_JOIN",target,sizeof(target));
    if (!length) return;
    if (length>=sizeof(target)) ExitProcess(ERROR_INVALID_DATA);
    auto colon=strchr(target,':');
    if (!colon) ExitProcess(ERROR_INVALID_DATA);
    *colon++=0;
    char* end=nullptr;
    const auto port=strtoul(colon,&end,10);
    auto& address=reinterpret_cast<sockaddr_in&>(joinAddress);
    address.sin_family=AF_INET;
    if (!*colon || *end || !port || port>65535 || inet_pton(AF_INET,target,&address.sin_addr)!=1 || !LocalAddress(address.sin_addr.s_addr)) ExitProcess(ERROR_INVALID_DATA);
    address.sin_port=htons(static_cast<unsigned short>(port));
    joining=true;
}

// Run after XLiveInitializeEx has loaded its normal broadcast configuration.
// DiRT 2 system-link uses title packets, not XLLN's session invitation API.
void DiRT2VRLanBrowserSeedPeer() {
    if (!enabled || !joining) return;
    joining=false;
    XllnNetworkBroadcastEntity::BROADCAST_ENTITY peer{};
    peer.sockaddr=joinAddress;
    peer.entityType=XllnNetworkBroadcastEntity::TYPE::XLLN_NBE_UNKNOWN;
    EnterCriticalSection(&xlln_critsec_network_broadcast_addresses);
    xlln_network_broadcast_addresses.push_back(peer);
    LeaveCriticalSection(&xlln_critsec_network_broadcast_addresses);
}

namespace {
void PollDiscovery() {
    if (!xlln_network_instance_port) return;
    if (!attempted) {
        attempted=true;
        browser=socket(AF_INET,SOCK_DGRAM,IPPROTO_UDP);
        if (browser==INVALID_SOCKET) return;
        BOOL exclusive=TRUE;
        setsockopt(browser,SOL_SOCKET,SO_EXCLUSIVEADDRUSE,reinterpret_cast<char*>(&exclusive),sizeof(exclusive));
        sockaddr_in local{}; local.sin_family=AF_INET; local.sin_port=htons(discoveryPort);
        u_long nonblocking=1;
        if (bind(browser,reinterpret_cast<sockaddr*>(&local),sizeof(local)) || ioctlsocket(browser,FIONBIO,&nonblocking)) {
            closesocket(browser); browser=INVALID_SOCKET; return;
        }
    }
    if (browser==INVALID_SOCKET || GetTickCount64()<nextReply) return;
    Query query{}; sockaddr_in source{}; int sourceSize=sizeof(source);
    if (recvfrom(browser,reinterpret_cast<char*>(&query),sizeof(query),0,reinterpret_cast<sockaddr*>(&source),&sourceSize)!=sizeof(query)) return;
    if (memcmp(query.magic,"D2VRLAN?",8) || !LocalAddress(source.sin_addr.s_addr)) return;
    nextReply=GetTickCount64()+100;
    Reply reply{}; memcpy(reply.magic,"D2VRLAN!",8); reply.nonce=query.nonce; reply.protocol=2;
    reply.port=xlln_network_instance_port;
    bool found=false;
    EnterCriticalSection(&xlln_critsec_liveoverlan_broadcast);
    EnterCriticalSection(&xlive_critsec_xsession);
    if (const auto* live=xlive_xlocator_local_session) {
        const auto capacity=live->slotsPublicMaxCount+live->slotsPrivateMaxCount;
        if (capacity>=2 && capacity<=8) {
            reply.players=static_cast<uint32_t>(live->slotsPublicFilledCount+live->slotsPrivateFilledCount);
            reply.capacity=static_cast<uint32_t>(capacity);
            // XLocator advertises availability but does not expose the game's race state.
            reply.state=2;
            memcpy(&reply.session,&live->xnkid,8); found=true;
        }
    }
    for (const auto& entry:xlive_xsession_local_sessions) {
        if (found) break;
        const auto* session=entry.second; const auto* live=session->liveSession;
        if (!live || !(live->sessionFlags & XSESSION_CREATE_HOST) || !(live->sessionFlags & XSESSION_CREATE_USES_PEER_NETWORK) || session->eState==XSESSION_STATE_DELETED) continue;
        const auto capacity=live->slotsPublicMaxCount+live->slotsPrivateMaxCount;
        if (capacity<2 || capacity>8) continue;
        reply.players=static_cast<uint32_t>(live->slotsPublicFilledCount+live->slotsPrivateFilledCount);
        reply.capacity=static_cast<uint32_t>(capacity);
        reply.state=session->eState==XSESSION_STATE_LOBBY ? 0 : 1;
        memcpy(&reply.session,&live->xnkid,8); found=true; break;
    }
    LeaveCriticalSection(&xlive_critsec_xsession);
    LeaveCriticalSection(&xlln_critsec_liveoverlan_broadcast);
    if (!found && hosting) {
        // Explicit HOST launch intent, not proof that the user has created a game lobby.
        reply.state=3; reply.session=GetCurrentProcessId(); found=true;
    }
    if (!found) return;
    DWORD nameLength=sizeof(reply.host);
    if (!GetComputerNameA(reply.host,&nameLength)) strcpy_s(reply.host,"DiRT 2 host");
    sendto(browser,reinterpret_cast<const char*>(&reply),sizeof(reply),0,reinterpret_cast<sockaddr*>(&source),sourceSize);
}
}

void DiRT2VRLanBrowserStop() {
    if (worker) {
        SetEvent(stopEvent);
        WaitForSingleObject(worker,INFINITE);
        CloseHandle(worker); CloseHandle(stopEvent);
        worker=nullptr; stopEvent=nullptr;
    }
    if (browser!=INVALID_SOCKET) { closesocket(browser); browser=INVALID_SOCKET; }
    attempted=false;
}
