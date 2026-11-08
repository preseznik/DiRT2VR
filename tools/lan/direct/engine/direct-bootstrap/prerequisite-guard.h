#pragma once
#include <cstdint>

namespace prerequisite {
using U32 = std::uint32_t;
struct Objects { U32 manager{}, user{}, context{}; };
enum class Gate { Ready, Root, Owner, SaveService, Busy, Handler, Network, Manager, User, Provider, AlreadyInitialized, Changed };

// Read-only checks for the exact, cold UpdateTask callback. No heap search,
// frontend objects, inferred offsets, or fallback owner is accepted.
template<class Read>
Gate Inspect(Read read, U32 base, U32 self, Objects& out) {
    out = {};
    const auto at = [base](U32 va) { return base + va - 0x400000; };
    const auto table = [&](U32 p, U32 vt) {
        return p >= 0x10000 && p < 0x7fff0000 && read(p) == at(vt);
    };
    const U32 marker = read(at(0x1482D80));
    if (!table(marker, 0x132006C) || !table(marker - 0xB98, 0x1321CD0)) return Gate::Root;
    if (!table(self, 0x1329A48) || read(marker - 0xC) != self || read(self + 0x2CEC) != marker - 0xB98) return Gate::Owner;
    const U32 sdu = read(self + 0x2294);
    if (!table(sdu, 0x1328574)) return Gate::SaveService;
    if (read(sdu + 0x54)) return Gate::Busy;
    const U32 handler = read(sdu + 0x3C);
    if (!table(handler, 0x12F1320) || read(self + 0x228C) != handler || !table(handler + 0x150, 0x1375B40)) return Gate::Handler;
    const U32 network = read(self + 0x2CF0);
    if (!table(network, 0x1336604)) return Gate::Network;
    const U32 manager = read(network + 0x1088);
    if (!table(manager, 0x135CCF8) || read(at(0x14925E4)) != manager) return Gate::Manager;
    const U32 user = read(manager + 0x58);
    if (!table(user, 0x135C508) || read(user + 4) != user || read(user + 0xC) != 0) return Gate::User;
    const U32 provider = read(at(0x15DAA04));
    if (provider < 0x10000 || provider >= 0x7fff0000 || read(provider + 4) != 2 || !read(user + 0x30)) return Gate::Provider;
    if (read(manager + 0x54) != 0xffffffff || read(handler + 0x154) != 0xffffffff) return Gate::AlreadyInitialized;
    if (read(at(0x1482D80)) != marker || read(marker - 0xC) != self) return Gate::Changed;
    out = {manager, user, handler + 0x150};
    return Gate::Ready;
}
}
