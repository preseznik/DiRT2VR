// Export only the address and public session ID from the live host.
#include "native-peer-address-v1.h"
void ReportDirectHost() {
    static bool reported = false;
    if (reported || !hostObservedReady || hostObservationStopped ||
        Read(hostHolder + 8) != 1 || Read(hostHolder + 0x18) != hostSession ||
        Read(hostHolder + 0x24) != hostTransport || Read(hostTransport + 8) != 1 ||
        (Read(hostHolder + 0x800) & 0xff) || !(Read(hostSession + 0x6c) & 0xff) ||
        Read(hostSession + 0x24) != 0) return;

    const U32 address = Read(hostTransport + 0x44);
    native_peer_address::Token token{};
    unsigned long long xuid{};
    U32 instance{};
    if (!native_peer_address::ReadToken(Read, address, token) ||
        !NativeLocalIdentity(xuid, instance) || instance != native_peer_address::Instance(token) ||
        (Read(address + 36) & 0xffff) != (Read(hostTransport + 0x2c) & 0xffff)) return;
    U32 publicId[]{Read(hostHolder + 0x6d8), Read(hostHolder + 0x6dc)};
    if (!(publicId[0] | publicId[1]) || Read(address + 0x30) != publicId[0] ||
        Read(address + 0x34) != publicId[1] || Read(hostTransport + 0x44) != address) return;

    constexpr char hex[] = "0123456789abcdef";
    auto encode = [&](const unsigned char* bytes, size_t count, char* text) {
        for (size_t i = 0; i < count; ++i) {
            text[2 * i] = hex[bytes[i] >> 4];
            text[2 * i + 1] = hex[bytes[i] & 15];
        }
        text[2 * count] = 0;
    };
    char key[17]{}, addressText[77]{}, line[200]{};
    encode(reinterpret_cast<const unsigned char*>(publicId), sizeof(publicId), key);
    encode(token.data(), token.size(), addressText);
    sprintf_s(line, "DIRECT_HOST %016llx %s %s\r\n", xuid, key, addressText);
    Log(line);
    reported = true;
}
