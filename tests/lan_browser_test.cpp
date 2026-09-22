#include <winsock2.h>
#include <ws2tcpip.h>
#include <unknwn.h>
#include "xlive/xlive.hpp"
#include "xlive/xsession.hpp"
#include "xlive/xlocator.hpp"
#include "xlln/xlln-network.hpp"
#include <cstring>
#include <iostream>

CRITICAL_SECTION xlive_critsec_xsession, xlln_critsec_liveoverlan_broadcast, xlln_critsec_network_broadcast_addresses;
uint16_t xlln_network_instance_port=39000;
std::map<HANDLE,LIVE_SESSION_XSESSION*> xlive_xsession_local_sessions;
std::vector<XllnNetworkBroadcastEntity::BROADCAST_ENTITY> xlln_network_broadcast_addresses;
LIVE_SESSION* xlive_xlocator_local_session=nullptr;
void DiRT2VRLanBrowserConfigure();
void DiRT2VRLanBrowserSeedPeer();
void DiRT2VRLanBrowserStop();

int main() {
    WSADATA data{}; if (WSAStartup(MAKEWORD(2,2),&data)) return 1;
    InitializeCriticalSection(&xlive_critsec_xsession);
    InitializeCriticalSection(&xlln_critsec_liveoverlan_broadcast);
    InitializeCriticalSection(&xlln_critsec_network_broadcast_addresses);
    LIVE_SESSION locator{}; locator.slotsPublicMaxCount=8; locator.slotsPublicFilledCount=2;
    xlive_xlocator_local_session=&locator;
    SetEnvironmentVariableA("DIRT2VR_LAN_DISCOVERY","1");
    SetEnvironmentVariableA("DIRT2VR_LAN_HOST","1");
    SetEnvironmentVariableA("DIRT2VR_LAN_JOIN","127.0.0.1:39000");
    DiRT2VRLanBrowserConfigure();
    SOCKET client=socket(AF_INET,SOCK_DGRAM,IPPROTO_UDP);
    DWORD timeout=1000; setsockopt(client,SOL_SOCKET,SO_RCVTIMEO,reinterpret_cast<char*>(&timeout),sizeof(timeout));
    sockaddr_in target{}; target.sin_family=AF_INET; target.sin_port=htons(39821); target.sin_addr.s_addr=htonl(INADDR_LOOPBACK);
    char query[16]{}; memcpy(query,"D2VRLAN?",8); query[8]=42;
    char reply[104]{};
    // No game notification polling occurs: discovery must work while menus are idle.
    Sleep(100);
    sendto(client,query,sizeof(query),0,reinterpret_cast<sockaddr*>(&target),sizeof(target));
    if (recv(client,reply,sizeof(reply),0)!=sizeof(reply) || memcmp(reply,"D2VRLAN!",8) || reply[8]!=42 || reply[16]!=2 || reply[22]!=2 || reply[24]!=2 || reply[28]!=8) return 2;
    xlln_network_broadcast_addresses.emplace_back(); // Preserve an existing configured peer.
    DiRT2VRLanBrowserSeedPeer(); DiRT2VRLanBrowserSeedPeer();
    if (xlln_network_broadcast_addresses.size()!=2) return 3;
    const auto& peer=reinterpret_cast<const sockaddr_in&>(xlln_network_broadcast_addresses.back().sockaddr);
    if (peer.sin_family!=AF_INET || peer.sin_port!=htons(39000) || peer.sin_addr.s_addr!=htonl(INADDR_LOOPBACK)) return 4;
    EnterCriticalSection(&xlln_critsec_liveoverlan_broadcast);
    xlive_xlocator_local_session=nullptr;
    LeaveCriticalSection(&xlln_critsec_liveoverlan_broadcast);
    Sleep(120);
    sendto(client,query,sizeof(query),0,reinterpret_cast<sockaddr*>(&target),sizeof(target));
    if (recv(client,reply,sizeof(reply),0)!=sizeof(reply) || reply[22]!=3 || reply[24]!=0 || reply[28]!=0) return 5;
    DiRT2VRLanBrowserStop();
    SetEnvironmentVariableA("DIRT2VR_LAN_HOST","0");
    SetEnvironmentVariableA("DIRT2VR_LAN_JOIN",nullptr);
    DiRT2VRLanBrowserConfigure();
    Sleep(120);
    sendto(client,query,sizeof(query),0,reinterpret_cast<sockaddr*>(&target),sizeof(target));
    if (recv(client,reply,sizeof(reply),0)!=SOCKET_ERROR || WSAGetLastError()!=WSAETIMEDOUT) return 6;
    DiRT2VRLanBrowserStop();
    closesocket(client);
    DeleteCriticalSection(&xlive_critsec_xsession);
    DeleteCriticalSection(&xlln_critsec_liveoverlan_broadcast);
    DeleteCriticalSection(&xlln_critsec_network_broadcast_addresses);
    WSACleanup();
    std::cout << "Idle discovery, HOST intent, silent JOIN client, one-shot peer seeding and clean worker shutdown passed.\n";
}
