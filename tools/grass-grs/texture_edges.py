"""Edge and tiling checks for a venue's terrain textures and grass card atlas (read-only).

usage: python -I texture_edges.py <venue folder> <report.json>

Terrain textures (tracksplit.pssg): mean luminance of an 8-texel border band
against the interior, and the wrap seam (left/right and top/bottom texel
differences) against the texture's own neighbouring-texel difference.
Grass atlas (ground_cover.pssg + grass.grs card rectangles): alpha coverage on
each card's rectangle edge, and opaque texels from other cards within 8 texels
outside it (bleed under mipmapping).
"""
import io, json, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import grassgrs, pssgterrain
from PIL import Image

FOURCC = {'dxt1': b'DXT1', 'dxt3': b'DXT3', 'dxt5': b'DXT5'}


def textures(path):
    P = pssgterrain.Pssg(path)
    for t in P.root.walk():
        if t.name != 'TEXTURE':
            continue
        fmt, w, h = P.text(t, 'texelFormat'), P.uint(t, 'width'), P.uint(t, 'height')
        if fmt not in FOURCC:
            continue
        data = next(n for n in t.walk() if n.name == 'TEXTUREIMAGEBLOCKDATA').data
        size = max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * (8 if fmt == 'dxt1' else 16)
        header = bytearray(128)
        struct.pack_into('<4sIIIIIII', header, 0, b'DDS ', 124, 0x81007, h, w, size, 0, 1)
        struct.pack_into('<II4s', header, 76, 32, 4, FOURCC[fmt])
        struct.pack_into('<I', header, 108, 0x1000)
        yield P.text(t, 'id'), Image.open(io.BytesIO(bytes(header) + P.b[data[0]:data[0] + size])).convert('RGBA')


def luminance(px):
    return .2126 * px[0] + .7152 * px[1] + .0722 * px[2]


def terrain_edges(image, band=8, step=2):
    w, h = image.size
    px = image.load()
    border = interior = 0.0
    nb = ni = 0
    for y in range(0, h, step):
        for x in range(0, w, step):
            l = luminance(px[x, y])
            if min(x, y, w - 1 - x, h - 1 - y) < band:
                border += l; nb += 1
            elif band * 4 <= x < w - band * 4 and band * 4 <= y < h - band * 4:
                interior += l; ni += 1
    seam = [abs(luminance(px[0, y]) - luminance(px[w - 1, y])) for y in range(h)] + [abs(luminance(px[x, 0]) - luminance(px[x, h - 1])) for x in range(w)]
    inner = [abs(luminance(px[x, y]) - luminance(px[x + 1, y])) for y in range(0, h, 7) for x in range(w // 4, 3 * w // 4)]
    return {'Size': [w, h], 'BorderVsInterior': round((border / nb) / (interior / ni) - 1, 4),
            'WrapSeam': round(sum(seam) / len(seam), 2), 'NeighbourDifference': round(sum(inner) / len(inner), 2)}


def card_edges(atlas, grass):
    w, h = atlas.size
    px = atlas.load()
    out = []
    for t, (name, variants) in enumerate(grass.type_names()):
        flag, cards = grass.appearance(t)
        for v, (cw, ch, u0, v0, u1, v1) in enumerate(cards):
            x0, x1 = sorted((round(u0 * w), round(u1 * w))); y0, y1 = sorted((round(v0 * h), round(v1 * h)))
            x1, y1 = min(x1, w) - 1, min(y1, h) - 1
            edge = [px[x, y][3] for x in range(x0, x1 + 1) for y in (y0, y1)] + [px[x, y][3] for y in range(y0, y1 + 1) for x in (x0, x1)]
            top = [px[x, y0][3] for x in range(x0, x1 + 1)]
            outside = 0
            for d in range(1, 9):
                for x in range(max(0, x0 - 8), min(w, x1 + 9)):
                    for y in (y0 - d, y1 + d):
                        if 0 <= y < h and px[x, y][3] > 128:
                            outside += 1
                for y in range(max(0, y0 - 8), min(h, y1 + 9)):
                    for x in (x0 - d, x1 + d):
                        if 0 <= x < w and px[x, y][3] > 128:
                            outside += 1
            out.append({'Type': name, 'Variant': variants[v] if v < len(variants) else v, 'Rect': [x0, y0, x1, y1],
                        'EdgeOpaqueFraction': round(sum(a > 128 for a in edge) / len(edge), 3),
                        'TopEdgeOpaqueFraction': round(sum(a > 128 for a in top) / len(top), 3),
                        'OpaqueTexelsWithin8Outside': outside})
    return out


def main(venue, report):
    out = {'Terrain': {}, 'Cards': []}
    for name, image in textures(os.path.join(venue, 'tracksplit.pssg')):
        if any(k in name for k in ('_inf_', '_grs_', 'grass', '_bas_', '_edg_', 'infield')):
            out['Terrain'][name] = terrain_edges(image)
    grass = grassgrs.read(os.path.join(venue, 'grass.grs'))
    for _, atlas in textures(os.path.join(venue, 'ground_cover.pssg')):
        out['Cards'] = card_edges(atlas, grass)
    json.dump(out, open(report, 'w'), indent=1)
    for k, v in sorted(out['Terrain'].items(), key=lambda kv: -abs(kv[1]['BorderVsInterior'])):
        print('%-28s %s' % (k, v))
    for c in out['Cards']:
        if c['EdgeOpaqueFraction'] > .02 or c['OpaqueTexelsWithin8Outside']:
            print(c)


if __name__ == '__main__':
    main(*sys.argv[1:])
