"""Bind the three XLLN instruction windows to the private host or client link."""
from pathlib import Path
import json
import struct
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[2] / ".deps/python"))
import pefile

# Ordinal, offset from export, DiRT 2 import slot, base relocation offsets.
WINDOWS = (
    (54, 0, 0x128C6E8, (14, 23, 28, 42, 51, 56, 61, 94)),
    (73, 0x89, 0x128C850, (2, 27, 33, 77)),
    (64, 0xBE, 0x128C878, ()),
)
def reference_bytes():
    return [bytes.fromhex(row) for row in json.loads((HERE / "provider-windows.json").read_bytes())]


def linked_windows(dll):
    with pefile.PE(str(dll)) as pe:
        exports = {s.ordinal: s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols if not s.forwarder}
        reloc = {e.rva: e.type for block in pe.DIRECTORY_ENTRY_BASERELOC for e in block.entries if e.type}
        output = []
        for (ordinal, offset, _, slots), ref in zip(WINDOWS, reference_bytes(), strict=True):
            start = exports[ordinal] + offset
            data = bytearray(pe.get_data(start, len(ref)))
            if sorted((r-start, t) for r, t in reloc.items() if start <= r < start+len(ref)) != [(p, 3) for p in slots]:
                raise ValueError(f"Unexpected base relocations in export {ordinal}")
            compare = bytearray(data)
            for p in slots:
                target = struct.unpack_from("<I", data, p)[0] - pe.OPTIONAL_HEADER.ImageBase
                if not pe.get_section_by_rva(target):
                    raise ValueError(f"Operand outside provider in export {ordinal}")
                struct.pack_into("<I", data, p, target)
                compare[p:p+4] = ref[p:p+4]
            if ordinal == 54:
                # The error-report call moves when the provider's other objects move.
                call = ref.index(bytes.fromhex("e8 43 7e 04 00"))
                target = start + call + 5 + struct.unpack_from("<i", data, call+1)[0]
                section = pe.get_section_by_rva(target)
                if not section or not section.Characteristics & 0x20000000:
                    raise ValueError("XNetCreateKey call target outside code")
                compare[call+1:call+5] = ref[call+1:call+5]
            if compare != ref:
                raise ValueError(f"Changed instruction in provider export {ordinal}")
            output.append(data)
        return output


def expected_include(windows):
    return "".join("{" + ",".join(f"0x{n:02x}" for n in row) + "},\n" for row in windows)


def main():
    action, role = sys.argv[1:]
    if role not in ("host", "client") or action not in ("init", "bind", "verify"):
        raise ValueError("Expected init/bind/verify and host/client")
    build = HERE.parents[2] / "build" / ("direct-" + role)
    build.mkdir(parents=True, exist_ok=True)
    include = build / "direct-peer-bytes.inc"
    if action == "init" and include.exists():
        return
    rows = reference_bytes() if action == "init" else linked_windows(build / "payload/xlive.dll")
    contents = expected_include(rows)
    if include.exists() and contents == include.read_text():
        return
    if action == "verify":
        raise ValueError(f"{role} provider layout changed after relink")
    include.write_text(contents, encoding="ascii")


if __name__ == "__main__":
    main()
