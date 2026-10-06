using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace DiRT2VR.Nordschleife;

internal sealed record Material(string Id, string Name, string Shader, byte Blend, bool AlphaTest, Dictionary<string, string> Textures)
{
    internal Dictionary<string,float[]> Properties { get; init; } = new(StringComparer.Ordinal);
}
internal sealed record Mesh(string Name, Material Material, bool Physical, Vector3[] Positions, Vector3[] Normals, Vector2[] UV, Vector3[] Tangents, ushort[] Indices)
{
    internal bool Visible { get; init; } = true;
    internal bool Active { get; init; } = true;
    internal bool Renderable { get; init; } = true;
    internal int SourceNode { get; init; }
    internal float LodIn { get; init; }
    internal float LodOut { get; init; }
}
internal sealed record Texture(byte[] Dds, string Hash);

// Read one mesh at a time; do not retain the complete seven-million-vertex scene.
internal static class Kn5
{
    internal static void Read(string path, Matrix4x4 placement, Dictionary<string, Texture> textures, Action<Mesh> consume, bool includeInactive = false)
    {
        Files.NoLinks(path);
        using var stream = File.OpenRead(path);
        using var r = new BinaryReader(stream, Encoding.UTF8);
        byte[] Bytes(int n)
        {
            if (n < 0 || n > stream.Length - stream.Position) throw new InvalidDataException("Truncated KN5: " + path);
            var value = r.ReadBytes(n); if (value.Length != n) throw new EndOfStreamException(); return value;
        }
        int Count(int max, string kind)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > max) throw new InvalidDataException("Invalid KN5 " + kind + " count: " + n);
            return n;
        }
        string Text() => new UTF8Encoding(false, true).GetString(Bytes(Count(65536, "string")));
        Vector3 V3() => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        if (Encoding.ASCII.GetString(Bytes(6)) != "sc6969") throw new InvalidDataException("Not a KN5 file: " + path);
        int version = r.ReadInt32();
        if (version is not (5 or 6)) throw new InvalidDataException("Unsupported KN5 version: " + version);
        if (version == 6) r.ReadInt32();
        int textureCount = Count(4096, "texture");
        for (int i = 0; i < textureCount; i++)
        {
            r.ReadInt32(); string name = Text(); var data = Bytes(Count(64 * 1024 * 1024, "texture byte"));
            string hash = Convert.ToHexString(SHA256.HashData(data));
            if (textures.TryGetValue(name, out var old) && old.Hash != hash) throw new InvalidDataException("Conflicting texture payload: " + name);
            textures.TryAdd(name, new(data, hash));
        }
        var materials = new Material[Count(4096, "material")];
        for (int i = 0; i < materials.Length; i++)
        {
            string name = Text(), shader = Text(); byte blend = r.ReadByte(); bool alpha = r.ReadBoolean(); r.ReadUInt32();
            int properties = Count(4096, "property");
            var values=new Dictionary<string,float[]>(StringComparer.Ordinal);
            for (int j = 0; j < properties; j++)
            {
                string property=Text();var data=new float[10];for(int k=0;k<data.Length;k++)data[k]=r.ReadSingle();
                if(data.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Nonfinite material property: "+property);
                values.Add(property,data);
            }
            int mappings = Count(64, "mapping"); var slots = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int j = 0; j < mappings; j++) { string slot = Text(); r.ReadUInt32(); slots.Add(slot, Text()); }
            materials[i] = new(Path.GetFileName(path) + ":" + i, name, shader, blend, alpha, slots){Properties=values};
        }
        int nodes = 0;
        void Node(Matrix4x4 parent, bool enabled, int depth)
        {
            if (depth > 64 || ++nodes > 100000) throw new InvalidDataException("KN5 node hierarchy exceeds limits.");
            int sourceNode = nodes;
            int kind = r.ReadInt32(); string name = Text(); int children = Count(100000, "child"); enabled &= r.ReadBoolean();
            var world = parent;
            if (kind == 1)
            {
                var m = new float[16]; for (int i = 0; i < 16; i++) m[i] = r.ReadSingle();
                if (m.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Nonfinite node transform.");
                world = new Matrix4x4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]) * parent;
            }
            else if (kind == 2)
            {
                r.ReadBoolean(); bool visible = r.ReadBoolean(); r.ReadBoolean();
                int nv = Count(65536, "vertex");
                var positions = new Vector3[nv]; var normals = new Vector3[nv]; var uv = new Vector2[nv]; var tangents = new Vector3[nv];
                if (!Matrix4x4.Invert(world, out var inverse)) throw new InvalidDataException("Singular mesh transform.");
                var normalMatrix = Matrix4x4.Transpose(inverse);
                for (int i = 0; i < nv; i++)
                {
                    positions[i] = Vector3.Transform(V3(), world);
                    var normal = Vector3.TransformNormal(V3(), normalMatrix);
                    if (!Finite(normal)) throw new InvalidDataException("Nonfinite normal: " + name);
                    normals[i] = normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : Vector3.UnitY;
                    uv[i] = new(r.ReadSingle(), r.ReadSingle()); tangents[i] = Vector3.TransformNormal(V3(), world);
                    if (!Finite(positions[i]) || !Finite(normals[i]) || !Finite(tangents[i]) || !float.IsFinite(uv[i].X) || !float.IsFinite(uv[i].Y))
                        throw new InvalidDataException("Nonfinite mesh data: " + name);
                }
                int ni = Count(4000000, "index"); if (ni % 3 != 0) throw new InvalidDataException("Nontriangular mesh: " + name);
                var indices = new ushort[ni];
                for (int i = 0; i < ni; i++) { indices[i] = r.ReadUInt16(); if (indices[i] >= nv) throw new InvalidDataException("Index outside mesh: " + name); }
                int material = Count(materials.Length - 1, "material index"); r.ReadUInt32();
                float lodIn = r.ReadSingle(), lodOut = r.ReadSingle(); Bytes(16); bool renderable = r.ReadBoolean();
                if (!float.IsFinite(lodIn) || !float.IsFinite(lodOut)) throw new InvalidDataException("Nonfinite mesh LOD: " + name);
                bool physical = name.Length > 0 && char.IsAsciiDigit(name[0]);
                if (includeInactive || enabled && (physical || visible && renderable))
                {
                    if (world.GetDeterminant() < 0) for (int i = 0; i < ni; i += 3) (indices[i+1], indices[i+2]) = (indices[i+2], indices[i+1]);
                    consume(new(name, materials[material], physical, positions, normals, uv, tangents, indices)
                    {Visible=enabled&&visible&&renderable,Active=enabled,Renderable=renderable,SourceNode=sourceNode,LodIn=lodIn,LodOut=lodOut});
                }
            }
            else throw new InvalidDataException("Unsupported KN5 node class: " + kind);
            for (int i = 0; i < children; i++) Node(world, enabled, depth + 1);
        }
        Node(placement, true, 0);
        if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected KN5 trailing data: " + path);
    }
    internal static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
