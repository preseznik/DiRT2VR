"""Estimate the native grass load around a course (clumps and cells within a radius).

usage: python -I load.py <grass.grs> <terrain.pssg> <road material or shader group> [radius ...]

Emulates the scatter of 0xd77990 without jitter: grid step = mean spacing of the
three vertices' primary types (millimetre grid, world aligned), one clump per
grid point inside the triangle; a second pass with vertex 0's secondary type
when every vertex has primary != secondary. Counts are estimates for
comparing tracks against the VR budgets (50,000 clumps / 512 cells per view,
65,534 clumps / 600 cells shared), not exact runtime numbers.
"""
import math, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import grassgrs, pssgterrain


def clumps(a, b, c, step):
    xs, zs = (a.x, b.x, c.x), (a.z, b.z, c.z)
    if step <= 0:
        return 0
    def edge(p, q, x, z):
        return (q[0] - p[0]) * (z - p[1]) - (q[1] - p[1]) * (x - p[0])
    P = [(a.x, a.z), (b.x, b.z), (c.x, c.z)]
    area = edge(P[0], P[1], *P[2])
    if abs(area) < 1e-9:
        return 0
    n = 0
    for i in range(math.ceil(min(xs) / step), math.floor(max(xs) / step) + 1):
        for j in range(math.ceil(min(zs) / step), math.floor(max(zs) / step) + 1):
            x, z = i * step, j * step
            w = [edge(P[1], P[2], x, z), edge(P[2], P[0], x, z), edge(P[0], P[1], x, z)]
            if all(v * area >= 0 for v in w):
                n += 1
    return n


def cell_loads(grass):
    spacing = [grass.scatter(t)[0] for t in range(8)]
    out = []
    for index, cell in enumerate(grass.cells):
        v = [grassgrs.decode_vertex(r, cell.bmin, cell.bmax) for r in cell.vertices]
        total = 0
        for t in cell.triangles:
            a, b, c = (v[i] for i in t)
            total += clumps(a, b, c, round(sum(spacing[p.primary] for p in (a, b, c)) / 3, 3))
            if all(p.primary != p.secondary for p in (a, b, c)):
                total += clumps(a, b, c, round(spacing[a.secondary], 3))
        cx, cz = (cell.bmin[0] + cell.bmax[0]) / 2, (cell.bmin[2] + cell.bmax[2]) / 2
        out.append((index, cx, cz, total))
    return out


def course(terrain, road, step=25):
    seen = {}
    for material, group, tri in pssgterrain.triangles(terrain):
        if material == road or group == road:
            for p, _ in tri:
                seen.setdefault((round(p[0] / step), round(p[2] / step)), (p[0], p[2]))
    return list(seen.values())


def window(loads, samples, radius):
    rows = []
    for x, z in samples:
        near = [l for l in loads if (l[1] - x) ** 2 + (l[2] - z) ** 2 <= radius * radius]
        rows.append((sum(l[3] for l in near), len(near)))
    rows.sort()
    n = len(rows)
    cells = sorted(r[1] for r in rows)
    return {'Radius': radius, 'ClumpsP50': rows[n // 2][0], 'ClumpsP90': rows[n * 9 // 10][0], 'ClumpsMax': rows[-1][0],
            'CellsP50': cells[n // 2], 'CellsP90': cells[n * 9 // 10], 'CellsMax': cells[-1]}


def main(grass_path, terrain, road, *radii):
    loads = cell_loads(grassgrs.read(grass_path))
    samples = course(terrain, road)
    print('cells', len(loads), 'clumps total', sum(l[3] for l in loads), 'course samples', len(samples))
    for r in (radii or ('150', '250')):
        print(window(loads, samples, float(r)))


if __name__ == '__main__':
    main(*sys.argv[1:])
