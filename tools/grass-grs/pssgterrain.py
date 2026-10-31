"""Minimal read-only PSSG reader for terrain triangles (positions, vertex colour, material)."""
import struct

DATA_NODES = {'DATABLOCKDATA', 'INDEXSOURCEDATA', 'TEXTUREIMAGEBLOCKDATA', 'SHADERINPUT', 'TRANSFORM', 'BOUNDINGBOX',
              'CGSTREAM', 'SHADERPROGRAMCODEBLOCK', 'KEYS', 'SHADERPROGRAMCODE', 'INVERSEBINDMATRIX', 'BINDSHAPEMATRIX',
              'WEIGHTS', 'RENDERINTERFACEBOUNDBUFFERED', 'ANIMATIONCHANNELDATABLOCK', 'SHADERGROUPPASS'}
IDENTITY = (1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)


class Node:
    __slots__ = ('name', 'attrs', 'children', 'data')

    def __init__(self, name):
        self.name, self.attrs, self.children, self.data = name, {}, [], None

    def walk(self):
        stack = [self]
        while stack:
            n = stack.pop()
            yield n
            stack.extend(reversed(n.children))


class Pssg:
    def __init__(self, path):
        b = self.b = open(path, 'rb').read()
        if b[:4] != b'PSSG':
            raise ValueError('not an uncompressed PSSG file: ' + path)
        _, nodes = struct.unpack_from('>II', b, 8)
        p, self.nodes, self.attrs = 16, {}, {}
        for _ in range(nodes):
            nid, ln = struct.unpack_from('>II', b, p); p += 8
            self.nodes[nid] = b[p:p + ln].decode(); p += ln
            na, = struct.unpack_from('>I', b, p); p += 4
            for _ in range(na):
                aid, ln = struct.unpack_from('>II', b, p); p += 8
                self.attrs[aid] = b[p:p + ln].decode(); p += ln
        self.root = self._parse(p)[0]

    def _parse(self, p):
        b = self.b
        nid, size, asz = struct.unpack_from('>III', b, p)
        n = Node(self.nodes.get(nid, '?'))
        end, q = p + 8 + size, p + 12
        while q < p + 12 + asz:
            aid, ln = struct.unpack_from('>II', b, q); q += 8
            n.attrs[self.attrs.get(aid, '?')] = b[q:q + ln]; q += ln
        if n.name in DATA_NODES:
            n.data = (q, end)
            return n, end
        r = q
        while r < end:
            if r + 12 > end:
                break
            cid, csz, casz = struct.unpack_from('>III', b, r)
            if cid not in self.nodes or r + 8 + csz > end or casz > csz:
                break
            r += 8 + csz
        if r == end:
            r = q
            while r < end:
                c, r = self._parse(r)
                n.children.append(c)
        else:
            n.data = (q, end)
        return n, end

    def text(self, n, k):
        v = n.attrs.get(k)
        if v is None or len(v) < 4 or struct.unpack_from('>I', v, 0)[0] != len(v) - 4:
            return None
        return v[4:].decode('latin1')

    def uint(self, n, k):
        v = n.attrs.get(k)
        return struct.unpack('>I', v)[0] if v is not None and len(v) == 4 else None


def _mul(a, b):
    return tuple(sum(a[r * 4 + k] * b[k * 4 + c] for k in range(4)) for r in range(4) for c in range(4))


def triangles(path):
    """Yield (material id, shader group, [(pos, colour bytes) x 3]) for every triangle-list mesh."""
    P = Pssg(path)
    blocks = {}
    for d in P.root.walk():
        if d.name != 'DATABLOCK':
            continue
        n = P.uint(d, 'elementCount')
        streams = {P.text(s, 'renderType'): (P.text(s, 'dataType'), P.uint(s, 'offset'), P.uint(s, 'stride'))
                   for s in d.children if s.name == 'DATABLOCKSTREAM'}
        data = next(c for c in d.children if c.name == 'DATABLOCKDATA').data[0]
        if streams.get('Vertex', ('',))[0] != 'float3':
            continue
        _, off, st = streams['Vertex']
        pos = [struct.unpack_from('>3f', P.b, data + off + i * st) for i in range(n)]
        col = None
        if 'Color' in streams:
            _, off, st = streams['Color']
            col = [P.b[data + off + i * st:data + off + i * st + 4] for i in range(n)]
        blocks[P.text(d, 'id')] = (pos, col)
    sources = {}
    for r in P.root.walk():
        if r.name != 'RENDERDATASOURCE':
            continue
        idx, dbs = None, []
        for c in r.children:
            if c.name == 'RENDERINDEXSOURCE':
                cnt, fmt = P.uint(c, 'count'), P.text(c, 'format')
                data = next(x for x in c.children if x.name == 'INDEXSOURCEDATA').data[0]
                idx = (P.text(c, 'primitive'), struct.unpack_from('>%d%s' % (cnt, 'H' if fmt == 'ushort' else 'I'), P.b, data))
            elif c.name == 'RENDERSTREAM':
                dbs.append(P.text(c, 'dataBlock')[1:])
        sources[P.text(r, 'id')] = (idx, [blocks[d] for d in dict.fromkeys(dbs) if d in blocks])
    groups = {}
    for s in P.root.walk():
        if s.name == 'SHADERINSTANCE':
            groups[P.text(s, 'id')] = P.text(s, 'shaderGroup')

    def visit(node, world):
        if node.name in ('NODE', 'RENDERNODE', 'ROOTNODE'):
            for c in node.children:
                if c.name == 'TRANSFORM':
                    world = _mul(struct.unpack_from('>16f', P.b, c.data[0]), world)
        for c in node.children:
            if c.name == 'RENDERSTREAMINSTANCE':
                shader = P.text(c, 'shader')[1:]
                for src in c.children:
                    if src.name != 'RENDERINSTANCESOURCE':
                        continue
                    (prim, idx), vbs = sources[P.text(src, 'source')[1:]]
                    if prim != 'triangles' or not vbs:
                        continue
                    pos, col = vbs[0]
                    if world != IDENTITY:
                        pos = [tuple(p[0] * world[k] + p[1] * world[4 + k] + p[2] * world[8 + k] + world[12 + k] for k in range(3)) for p in pos]
                    for i in range(0, len(idx) - 2, 3):
                        t = idx[i:i + 3]
                        yield shader, groups.get(shader), [(pos[j], col[j] if col else None) for j in t]
            elif c.children:
                yield from visit(c, world)

    yield from visit(P.root, IDENTITY)
