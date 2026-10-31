"""DiRT 2 grass.grs (version 4) reader and writer.

Layout taken from dirt2_game.exe (SHA-256 49B1E00E...FE48): loader 0xd81f27,
vertex decode 0xd93640, per-triangle scatter 0xd77990, variant pick 0xd62da0.
See docs/grass-grs.md for the field evidence.
"""
import struct

VERSION = 4
TYPES = 8
APPEARANCE = 0x010   # 8 x 200 bytes: u32 flag, u32 variants, 8 x {w, h, u0, v0, u1, v1}
SCATTER = 0x650      # 8 x 104 bytes: f32 spacing, f32 p1, 8 x {scale min, scale max, frequency}
NAMES = 0x990        # 8 x 156 bytes: 8 x char[16] variant, u32 count, u32 flag, f32 0.5, char[16] type
ACTIVE = 0xE70       # u32 active type count
OFFSETS = 0xE74      # u32 cell offsets (absolute)
CELL_HEADER = 40     # u16 vc, u16 tc, 12 zero (runtime pointers), f32 min[3], f32 max[3]
VERTEX = 10
TRIANGLE = 6


class Vertex:
    __slots__ = ('x', 'y', 'z', 'primary', 'secondary', 'gradient', 'shade', 'tint')

    def __init__(self, x, y, z, primary=0, secondary=None, gradient=0, shade=0xBE, tint=0xFFFF):
        self.x, self.y, self.z = x, y, z
        self.primary = primary
        self.secondary = primary if secondary is None else secondary
        self.gradient = gradient   # 5 bits: variant position for flag=1 types
        self.shade = shade         # 8 bits: runtime uses shade >> 2 as instance colour alpha
        self.tint = tint           # RGB565 instance tint


class Cell:
    def __init__(self, bmin, bmax, vertices, triangles):
        self.bmin, self.bmax, self.vertices, self.triangles = bmin, bmax, vertices, triangles


class GrassFile:
    def __init__(self, library, cells, active):
        self.library = library     # bytes [0x10, 0xE70): appearance, scatter and name tables
        self.cells = cells
        self.active = active

    # Type library -------------------------------------------------------------
    def type_names(self):
        out = []
        for t in range(TYPES):
            o = NAMES - APPEARANCE + t * 156
            count = struct.unpack_from('<I', self.library, o + 128)[0]
            name = self.library[o + 140:o + 156].split(b'\0')[0].decode('ascii')
            variants = [self.library[o + 16 * j:o + 16 * j + 16].split(b'\0')[0].decode('ascii') for j in range(count)]
            out.append((name, variants))
        return out

    def appearance(self, t):
        o = t * 200
        flag, count = struct.unpack_from('<2I', self.library, o)
        return flag, [struct.unpack_from('<6f', self.library, o + 8 + 24 * v) for v in range(count)]

    def scatter(self, t):
        return struct.unpack_from('<26f', self.library, SCATTER - APPEARANCE + t * 104)


def _fraction(value, lo, hi):
    return 0.0 if hi <= lo else min(1.0, max(0.0, (value - lo) / (hi - lo)))


def decode_vertex(raw, bmin, bmax):
    x16, y16, zw = struct.unpack_from('<3H', raw, 0)
    tint = struct.unpack_from('<H', raw, 8)[0]
    return Vertex(bmin[0] + (bmax[0] - bmin[0]) * x16 / 65535,
                  bmin[1] + (bmax[1] - bmin[1]) * y16 / 65535,
                  bmin[2] + (bmax[2] - bmin[2]) * (zw & 0x1FFF) / 8191,
                  primary=zw >> 13, secondary=raw[6] & 7, gradient=raw[6] >> 3, shade=raw[7], tint=tint)


def encode_vertex(v, bmin, bmax):
    if not (0 <= v.primary < TYPES and 0 <= v.secondary < TYPES and 0 <= v.gradient < 32 and 0 <= v.shade < 256 and 0 <= v.tint < 65536):
        raise ValueError('vertex attribute out of range')
    x16 = round(_fraction(v.x, bmin[0], bmax[0]) * 65535)
    y16 = round(_fraction(v.y, bmin[1], bmax[1]) * 65535)
    z13 = round(_fraction(v.z, bmin[2], bmax[2]) * 8191)
    return struct.pack('<3HBBH', x16, y16, z13 | (v.primary << 13), v.secondary | (v.gradient << 3), v.shade, v.tint)


def read(path):
    b = open(path, 'rb').read()
    version, active, count, zero = struct.unpack_from('<4I', b, 0)
    if version != VERSION or zero != 0:
        raise ValueError('not a DiRT 2 version-4 grass file')
    if struct.unpack_from('<I', b, ACTIVE)[0] != active:
        raise ValueError('active type count mismatch')
    offsets = struct.unpack_from('<%dI' % count, b, OFFSETS)
    cells = []
    for i, o in enumerate(offsets):
        end = offsets[i + 1] if i + 1 < count else len(b)
        vc, tc = struct.unpack_from('<2H', b, o)
        if any(b[o + 4:o + 16]):
            raise ValueError('cell %d runtime pointer bytes are not zero' % i)
        bounds = struct.unpack_from('<6f', b, o + 16)
        vs, ts = o + CELL_HEADER, o + CELL_HEADER + vc * VERTEX
        te = ts + tc * TRIANGLE
        if o % 16 or te > end or end - te >= 16 or any(b[te:end]):
            raise ValueError('cell %d alignment or padding' % i)
        tris = [struct.unpack_from('<3H', b, ts + 6 * k) for k in range(tc)]
        if any(max(t) >= vc for t in tris):
            raise ValueError('cell %d index out of range' % i)
        cells.append(Cell(bounds[:3], bounds[3:], [b[vs + VERTEX * k:vs + VERTEX * (k + 1)] for k in range(vc)], tris))
    return GrassFile(b[APPEARANCE:ACTIVE], cells, active)


def write(path, library, active, cells):
    """cells: iterable of (vertices: [Vertex], triangles: [(i, j, k)])."""
    if len(library) != ACTIVE - APPEARANCE:
        raise ValueError('type library must be %d bytes' % (ACTIVE - APPEARANCE))
    packed = []
    for vertices, triangles in cells:
        if not triangles:
            continue
        bmin = [min(getattr(v, a) for v in vertices) for a in 'xyz']
        bmax = [max(getattr(v, a) for v in vertices) for a in 'xyz']
        raw, index, remap = [], {}, []
        for v in vertices:
            r = encode_vertex(v, bmin, bmax)
            if r not in index:
                index[r] = len(raw)
                raw.append(r)
            remap.append(index[r])
        tris = [tuple(remap[i] for i in t) for t in triangles]
        tris = [t for t in tris if len(set(t)) == 3]
        if len(raw) > 0xFFFF or len(tris) > 0xFFFF:
            raise ValueError('cell exceeds 16-bit counts')
        body = struct.pack('<2H12x6f', len(raw), len(tris), *bmin, *bmax) + b''.join(raw) + b''.join(struct.pack('<3H', *t) for t in tris)
        packed.append(body + b'\0' * (-len(body) % 16))
    head = struct.pack('<4I', VERSION, active, len(packed), 0) + library + struct.pack('<I', active)
    first = OFFSETS + 4 * len(packed)
    first += -first % 16
    offsets, cursor = [], first
    for body in packed:
        offsets.append(cursor)
        cursor += len(body)
    data = head + struct.pack('<%dI' % len(offsets), *offsets)
    data += b'\0' * (first - len(data)) + b''.join(packed)
    open(path, 'wb').write(data)
    return len(packed)


def donor_library(path):
    b = open(path, 'rb').read()
    if struct.unpack_from('<I', b, 0)[0] != VERSION:
        raise ValueError('donor is not version 4')
    return b[APPEARANCE:ACTIVE], struct.unpack_from('<I', b, 4)[0]
