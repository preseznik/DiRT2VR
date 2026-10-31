"""Add grass cells to a flat DiRT 2 track.vis (category 1, id = cell index, bounds = cell bounds).

usage: python -I visibility.py <track.vis in> <grass.grs> <track.vis out>

The game only generates grass for cells that track.vis lists in category 1
(stock Tanamera: 1,115 cells, 1,115 category-1 records with matching ids and
bounds). Supports the flat layout custom-track builders write: one all-visible
view leaf, one spatial node, raw all-ones mask (version 4, header counts at
64 + 4 * category). Stock hierarchical files are not rewritten.
"""
import struct, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import grassgrs

GRASS = 1


def add_grass(vis, grass):
    version, leaves, nodes, three, mask_bytes, view, mask, spatial = struct.unpack_from('<8I', vis, 0)
    counts = list(struct.unpack_from('<8I', vis, 64))
    count = struct.unpack_from('<I', vis, spatial + 28)[0] >> 16
    if (version, leaves, nodes, three, view, mask) != (4, 1, 1, 1, 128, 144) or len(vis) != spatial + 48 + 32 * count or spatial != mask + mask_bytes:
        raise ValueError('not a flat single-leaf track.vis')
    if counts[GRASS]:
        raise ValueError('track.vis already lists grass cells')
    records = [vis[spatial + 48 + 32 * i:spatial + 80 + 32 * i] for i in range(count)]
    grass_records = []
    for i, cell in enumerate(grass.cells):
        r = bytearray(32)
        struct.pack_into('<3fI3fI', r, 0, *cell.bmin, GRASS, *cell.bmax, i)
        grass_records.append(bytes(r))
    # Keep records ordered by category, as builders and stock write them.
    position = sum(1 for r in records if struct.unpack_from('<I', r, 12)[0] < GRASS)
    records = records[:position] + grass_records + records[position:]
    if len(records) > 0xFFFF:
        raise ValueError('too many visibility records')
    counts[GRASS] = len(grass_records)
    mask_bytes += (len(grass_records) + 127) // 128 * 16
    spatial = mask + mask_bytes
    lo = [min(struct.unpack_from('<3f', r, 0)[k] for r in records) for k in range(3)]
    hi = [max(struct.unpack_from('<3f', r, 16)[k] for r in records) for k in range(3)]
    out = bytearray(spatial + 48 + 32 * len(records))
    out[:128] = vis[:128]
    out[128:144] = vis[128:144]
    struct.pack_into('<8I', out, 0, version, leaves, nodes, three, mask_bytes, view, mask, spatial)
    struct.pack_into('<3f4x3f', out, 32, *lo, *hi)
    struct.pack_into('<8I', out, 64, *counts)
    out[mask:mask + mask_bytes] = b'\xff' * mask_bytes
    old_spatial = struct.unpack_from('<I', vis, 28)[0]
    out[spatial:spatial + 48] = vis[old_spatial:old_spatial + 48]
    struct.pack_into('<3f4x3f', out, spatial, *lo, *hi)
    struct.pack_into('<I', out, spatial + 28, len(records) << 16)
    for i, r in enumerate(records):
        out[spatial + 48 + 32 * i:spatial + 80 + 32 * i] = r
    return bytes(out), counts


def main(vis_in, grass_path, vis_out):
    data, counts = add_grass(open(vis_in, 'rb').read(), grassgrs.read(grass_path))
    open(vis_out, 'wb').write(data)
    print('categories', counts, 'bytes', len(data))


if __name__ == '__main__':
    main(*sys.argv[1:])
