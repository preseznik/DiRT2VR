"""Generate a DiRT 2 grass.grs for a custom track's own terrain (lab prototype).

usage: python -I generate.py <policy.json> <out-dir>

The policy names the built terrain PSSG files, the stock donor grass.grs whose
type library matches the track's ground_cover.pssg, the course (road)
materials, the grass materials with their vertex-colour weight byte, and
distance bands that paint grass types. Only the type library is taken from
the donor; every cell is built from the track's own triangles.
"""
import hashlib, heapq, json, math, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import grassgrs, pssgterrain


def sha256(path):
    return hashlib.sha256(open(path, 'rb').read()).hexdigest().upper()


def hash01(*k):
    h = 2166136261
    for v in k:
        h = ((h ^ (v & 0xFFFFFFFF)) * 16777619) & 0xFFFFFFFF
    h ^= h >> 13; h = (h * 0x5bd1e995) & 0xFFFFFFFF; h ^= h >> 15
    return h / 4294967296.0


def noise(x, z, scale, seed):
    """Bilinear value noise in [0, 1): coherent type patches about `scale` metres across."""
    fx, fz = x / scale, z / scale
    ix, iz = math.floor(fx), math.floor(fz)
    tx, tz = fx - ix, fz - iz
    tx, tz = tx * tx * (3 - 2 * tx), tz * tz * (3 - 2 * tz)
    a, b = hash01(ix, iz, seed), hash01(ix + 1, iz, seed)
    c, d = hash01(ix, iz + 1, seed), hash01(ix + 1, iz + 1, seed)
    return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz


def pick(choices, u):
    total = sum(w for _, w in choices)
    for value, w in choices:
        u -= w / total
        if u < 0:
            return value
    return choices[-1][0]


class DistanceField:
    """Approximate XZ distance to the course (road triangles) on a square grid, by Dijkstra."""

    def __init__(self, road_points, cell, limit):
        self.cell, self.limit, self.d = cell, limit, {}
        heap = []
        for x, z in road_points:
            k = (math.floor(x / cell), math.floor(z / cell))
            if k not in self.d:
                self.d[k] = 0.0
                heap.append((0.0, k))
        heapq.heapify(heap)
        steps = [(dx, dz, cell * math.hypot(dx, dz)) for dx in (-1, 0, 1) for dz in (-1, 0, 1) if dx or dz]
        while heap:
            dist, (i, j) = heapq.heappop(heap)
            if dist > self.d.get((i, j), math.inf):
                continue
            for dx, dz, w in steps:
                n, nd = (i + dx, j + dz), dist + w
                if nd <= limit + cell and nd < self.d.get(n, math.inf):
                    self.d[n] = nd
                    heapq.heappush(heap, (nd, n))

    def __call__(self, x, z):
        return self.d.get((math.floor(x / self.cell), math.floor(z / self.cell)), math.inf)


def main(policy_path, out):
    policy = json.load(open(policy_path))
    os.makedirs(out, exist_ok=True)
    library, active = grassgrs.donor_library(policy['donor'])
    names = {name: i for i, (name, _) in enumerate(grassgrs.GrassFile(library, [], active).type_names()) if name}
    # Optional density control: scale a type's grid spacing (scatter record float 0).
    library = bytearray(library)
    for name, scale in policy.get('spacingScale', {}).items():
        at = grassgrs.SCATTER - grassgrs.APPEARANCE + names[name] * 104
        struct.pack_into('<f', library, at, struct.unpack_from('<f', library, at)[0] * scale)
    library = bytes(library)
    seed = policy.get('seed', 1)

    source = []
    road = []
    road_materials = set(policy['course']['materials'])
    grass_materials = policy['materials']
    for path in policy['terrain']:
        for material, group, tri in pssgterrain.triangles(path):
            if material in road_materials:
                road.extend((p[0], p[2]) for p, _ in tri)
                road.append((sum(p[0] for p, _ in tri) / 3, sum(p[2] for p, _ in tri) / 3))
            elif material in grass_materials:
                source.append((material, tri))
    if not road:
        raise SystemExit('no course triangles found for ' + ', '.join(road_materials))
    corridor = policy['corridor']
    field = DistanceField(road, policy.get('fieldCell', 2.0), corridor)

    bands = policy['bands']
    def paint(x, z, distance):
        band = next((b for b in bands if distance <= b['until']), bands[-1])
        primary = names[pick(list(band['primary'].items()), noise(x, z, band.get('scale', 12), seed))]
        layer = pick(list(band.get('layer', {'same': 1}).items()), noise(x, z, band.get('layerScale', 9), seed + 7))
        secondary = primary if layer == 'same' else names[layer]
        jitter = policy.get('shadeJitter', 0)
        shade = max(0, min(255, round(policy['shade'] + (hash01(round(x * 4), round(z * 4), seed + 3) - .5) * 2 * jitter)))
        return grassgrs.Vertex(x, 0, z, primary, secondary, policy.get('gradient', 0), shade, int(policy['tint'], 0))

    max_slope = math.cos(math.radians(policy.get('maxSlopeDegrees', 40)))
    chosen, rejected = [], {'weight': 0, 'corridor': 0, 'slope': 0, 'degenerate': 0}
    for material, tri in source:
        rule = grass_materials[material]
        (a, ca), (b, cb), (c, cc) = tri
        weight = sum(col[rule['weightByte']] for col in (ca, cb, cc)) / 765 if rule.get('weightByte') is not None else 1.0
        if weight < rule.get('minWeight', 0):
            rejected['weight'] += 1; continue
        cx, cz = (a[0] + b[0] + c[0]) / 3, (a[2] + b[2] + c[2]) / 3
        if field(cx, cz) > corridor:
            rejected['corridor'] += 1; continue
        u = [b[k] - a[k] for k in range(3)]; v = [c[k] - a[k] for k in range(3)]
        n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        length = math.sqrt(sum(x * x for x in n))
        if length < 1e-6:
            rejected['degenerate'] += 1; continue
        if abs(n[1]) / length < max_slope:
            rejected['slope'] += 1; continue
        chosen.append((cx, cz, length / 2, tri))

    # Cells: whole triangles grouped by centroid square, split until small enough.
    size, limit = policy.get('cell', 16), policy.get('maxTrianglesPerCell', 256)
    def split(items, x0, z0, s):
        if len(items) <= limit or s <= 2:
            return [items]
        h = s / 2
        quads = {}
        for t in items:
            quads.setdefault((t[0] >= x0 + h, t[1] >= z0 + h), []).append(t)
        return [c for (qx, qz), q in sorted(quads.items()) for c in split(q, x0 + qx * h, z0 + qz * h, h)]
    squares = {}
    for t in chosen:
        squares.setdefault((math.floor(t[0] / size), math.floor(t[1] / size)), []).append(t)
    cells, sources, type_area = [], [], [0.0] * 8
    for (i, j), items in sorted(squares.items()):
        for group in split(items, i * size, j * size, size):
            vertices, triangles, positions = [], [], []
            for cx, cz, area, tri in group:
                base = len(vertices)
                for p, _ in tri:
                    v = paint(p[0], p[2], field(p[0], p[2]))
                    v.y = p[1]
                    vertices.append(v); positions.append(p)
                    type_area[v.primary] += area / 3
                triangles.append((base, base + 1, base + 2))
            cells.append((vertices, triangles)); sources.append(positions)
    path = os.path.join(out, 'grass.grs')
    written = grassgrs.write(path, library, active, cells)

    # Independent check: re-read and compare every decoded vertex with its source position.
    check = grassgrs.read(path)
    worst = 0.0
    for cell, (vertices, triangles), positions in zip(check.cells, cells, sources):
        decoded = [grassgrs.decode_vertex(r, cell.bmin, cell.bmax) for r in cell.vertices]
        for tri_out, tri_in in zip(cell.triangles, triangles):
            for o, s in zip(tri_out, tri_in):
                p, d = positions[s], decoded[o]
                worst = max(worst, math.dist(p, (d.x, d.y, d.z)))
                if (d.primary, d.secondary) != (vertices[s].primary, vertices[s].secondary):
                    raise SystemExit('type round trip failed')
    report = {
        'Output': path, 'Sha256': sha256(path), 'Bytes': os.path.getsize(path), 'Cells': written,
        'Triangles': sum(len(c.triangles) for c in check.cells), 'Vertices': sum(len(c.vertices) for c in check.cells),
        'MaxCellTriangles': max(len(c.triangles) for c in check.cells), 'MaxCellVertices': max(len(c.vertices) for c in check.cells),
        'MaxPositionErrorMetres': round(worst, 4), 'Rejected': rejected, 'GrassAreaM2': round(sum(t[2] for t in chosen)),
        'PrimaryTypeAreaM2': {grassgrs.GrassFile(library, [], active).type_names()[i][0]: round(a) for i, a in enumerate(type_area) if a},
        'Donor': {'Path': policy['donor'], 'Sha256': sha256(policy['donor'])},
        'Terrain': {p: sha256(p) for p in policy['terrain']}, 'Policy': policy,
    }
    report['Budget'] = budget(check, road)
    json.dump(report, open(os.path.join(out, 'grass-report.json'), 'w'), indent=2)
    draw_map(check, road, os.path.join(out, 'grass-map.png'))
    print(json.dumps({k: report[k] for k in ('Cells', 'Triangles', 'Vertices', 'MaxCellTriangles', 'MaxPositionErrorMetres', 'GrassAreaM2', 'PrimaryTypeAreaM2', 'Rejected', 'Budget', 'Bytes')}, indent=1))


def cell_triangles(grass):
    for index, cell in enumerate(grass.cells):
        decoded = [grassgrs.decode_vertex(r, cell.bmin, cell.bmax) for r in cell.vertices]
        for t in cell.triangles:
            a, b, c = (decoded[i] for i in t)
            u = (b.x - a.x, b.y - a.y, b.z - a.z); v = (c.x - a.x, c.y - a.y, c.z - a.z)
            area = .5 * math.sqrt((u[1] * v[2] - u[2] * v[1]) ** 2 + (u[2] * v[0] - u[0] * v[2]) ** 2 + (u[0] * v[1] - u[1] * v[0]) ** 2)
            yield index, (a.x + b.x + c.x) / 3, (a.z + b.z + c.z) / 3, area, (a, b, c)


def budget(grass, road, radius=150, step=25):
    """Grass substrate area and cell count within the ground-cover draw distance of course points."""
    samples = {}
    for x, z in road:
        samples.setdefault((round(x / step), round(z / step)), (x, z))
    tris = list(cell_triangles(grass))
    buckets = {}
    for t in tris:
        buckets.setdefault((math.floor(t[1] / radius), math.floor(t[2] / radius)), []).append(t)
    areas, counts = [], []
    for x, z in samples.values():
        area, cells = 0.0, set()
        kx, kz = math.floor(x / radius), math.floor(z / radius)
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for t in buckets.get((kx + dx, kz + dz), ()):
                    if (t[1] - x) ** 2 + (t[2] - z) ** 2 <= radius * radius:
                        area += t[3]; cells.add(t[0])
        areas.append(area); counts.append(len(cells))
    areas.sort(); counts.sort()
    n = len(areas)
    return {'CourseSamples': n, 'AreaWithin150mP50': round(areas[n // 2]), 'AreaWithin150mP90': round(areas[n * 9 // 10]),
            'AreaWithin150mMax': round(areas[-1]), 'CellsWithin150mP50': counts[n // 2], 'CellsWithin150mP90': counts[n * 9 // 10],
            'CellsWithin150mMax': counts[-1]}


COLOURS = [(230, 60, 60), (80, 220, 80), (60, 140, 255), (240, 220, 40), (230, 60, 230), (40, 220, 220), (255, 150, 30), (255, 255, 255)]


def draw_map(grass, road, path, scale=0.5):
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        return
    tris = list(cell_triangles(grass))
    xs = [p[0] for p in road] + [t[1] for t in tris]; zs = [p[1] for p in road] + [t[2] for t in tris]
    x0, z0 = min(xs) - 20, min(zs) - 20
    w, h = int((max(xs) + 20 - x0) * scale), int((max(zs) + 20 - z0) * scale)
    im = Image.new('RGB', (w, h), (25, 25, 25)); dr = ImageDraw.Draw(im)
    for x, z in road:
        im.putpixel((min(w - 1, int((x - x0) * scale)), min(h - 1, int((z - z0) * scale))), (150, 110, 70))
    for _, _, _, _, tri in tris:
        pts = [((v.x - x0) * scale, (v.z - z0) * scale) for v in tri]
        dr.polygon(pts, fill=COLOURS[tri[0].primary])
    im.transpose(Image.FLIP_TOP_BOTTOM).save(path)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
