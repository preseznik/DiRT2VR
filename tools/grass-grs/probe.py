"""Verify the documented grass.grs layout against stock DiRT 2 venues (read-only).

usage: python -I probe.py <DiRT 2 folder> <report.json> [venue ...]

Every tracks/**/grass.grs is parsed with grassgrs.read (version, offsets,
alignment, zero runtime bytes, padding, index ranges) and summarised. For the
named venues (e.g. malaysia/malaysia_iron) each grass vertex is matched to the
nearest vertex of the venue's tracksplit.pssg and route_*/routesplit.pssg, reporting the distance and the
terrain material it sits on.
"""
import collections, glob, json, math, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import grassgrs, pssgterrain

SKIP = {'#batched_track.fx', '#terrain_lod.fx'}


def summarise(path):
    g = grassgrs.read(path)
    flag, _ = g.appearance(0)
    vertices = [grassgrs.decode_vertex(r, c.bmin, c.bmax) for c in g.cells for r in c.vertices]
    count = collections.Counter
    prim, sec = count(v.primary for v in vertices), count(v.secondary for v in vertices)
    return {
        'ActiveTypes': g.active, 'Cells': len(g.cells), 'Vertices': len(vertices), 'Triangles': sum(len(c.triangles) for c in g.cells),
        'Type0AppearanceFlag': flag, 'MaxCellVertices': max(len(c.vertices) for c in g.cells), 'MaxCellTriangles': max(len(c.triangles) for c in g.cells),
        'Types': [name for name, _ in g.type_names()], 'PrimaryTypes': dict(sorted(prim.items())), 'SecondaryTypes': dict(sorted(sec.items())),
        'PrimaryEqualsSecondary': round(sum(v.primary == v.secondary for v in vertices) / len(vertices), 3),
        'DistinctTints': len(count(v.tint for v in vertices)), 'CommonTint': '0x%04X' % count(v.tint for v in vertices).most_common(1)[0][0],
        'ShadeP50': sorted(v.shade for v in vertices)[len(vertices) // 2], 'GradientZeroOr31': round(sum(v.gradient in (0, 31) for v in vertices) / len(vertices), 3),
    }


def terrain_match(game, venue):
    grid = collections.defaultdict(list)
    root = os.path.join(game, 'tracks', venue)
    for terrain in sorted(glob.glob(os.path.join(root, 'tracksplit.pssg')) + glob.glob(os.path.join(root, 'route_*', 'routesplit.pssg'))):
        for material, group, tri in pssgterrain.triangles(terrain):
            if group in SKIP:
                continue
            for p, _ in tri:
                grid[(round(p[0] * 2), round(p[2] * 2))].append((p, group))
    g = grassgrs.read(os.path.join(game, 'tracks', venue, 'grass.grs'))
    distances, groups = [], collections.Counter()
    for c in g.cells:
        for r in c.vertices:
            v = grassgrs.decode_vertex(r, c.bmin, c.bmax)
            best = (math.inf, None)
            kx, kz = round(v.x * 2), round(v.z * 2)
            for dx in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for p, group in grid.get((kx + dx, kz + dz), ()):
                        d = math.dist(p, (v.x, v.y, v.z))
                        if d < best[0]:
                            best = (d, group)
            distances.append(best[0])
            if best[0] < .02:
                groups[best[1]] += 1
    distances.sort()
    n = len(distances)
    return {'Vertices': n, 'MedianMetres': round(distances[n // 2], 4), 'P99Metres': round(distances[int(n * .99)], 4),
            'Within2cm': round(sum(d < .02 for d in distances) / n, 4), 'ShaderGroups': dict(groups.most_common())}


def main(game, report, *venues):
    files = sorted(glob.glob(os.path.join(game, 'tracks', '**', 'grass.grs'), recursive=True))
    out = {'Files': {}, 'Failures': {}, 'Terrain': {}}
    for path in files:
        rel = os.path.relpath(path, game).replace('\\', '/')
        try:
            out['Files'][rel] = summarise(path)
        except ValueError as error:
            out['Failures'][rel] = str(error)
    for venue in venues:
        out['Terrain'][venue] = terrain_match(game, venue)
    json.dump(out, open(report, 'w'), indent=1)
    print('%d files parsed, %d failures' % (len(out['Files']), len(out['Failures'])))
    for venue, m in out['Terrain'].items():
        print(venue, m)


if __name__ == '__main__':
    main(*sys.argv[1:])
