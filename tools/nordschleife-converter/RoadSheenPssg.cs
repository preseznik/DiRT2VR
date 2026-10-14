using System.Buffers.Binary;
using System.Text;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Nordschleife;

// Read only shader metadata and texture byte ranges. Loading a whole route into
// the general PSSG editor also expands hundreds of MB of unrelated mesh data.
internal static class RoadSheenPssg
{
    internal sealed record Node(string Name, Dictionary<string, byte[]> Attributes, long Data, int Bytes, List<Node> Children)
    {
        internal string Text(string key)
        {
            if (!Attributes.TryGetValue(key, out var bytes)) return "";
            if (bytes.Length < 4 || BinaryPrimitives.ReadInt32BigEndian(bytes) != bytes.Length - 4) throw new IOException("Invalid PSSG string.");
            return Encoding.UTF8.GetString(bytes, 4, bytes.Length - 4);
        }
        internal int Number(string key)
        {
            if (!Attributes.TryGetValue(key, out var bytes) || bytes.Length != 4) throw new IOException("Invalid PSSG integer.");
            return BinaryPrimitives.ReadInt32BigEndian(bytes);
        }
        internal IEnumerable<Node> All(string name) => Children.SelectMany(c => c.All(name)).Concat(Name == name ? new[] { this } : []);
    }
    internal static Node Read(string path)
    {
        SafeFiles.NoLinks(path);
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 16 || stream.Length > RoadSheen.MaxTextureBytes || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "PSSG") throw new IOException("Unsupported road PSSG.");
        int Int() { Span<byte> bytes = stackalloc byte[4]; stream.ReadExactly(bytes); return BinaryPrimitives.ReadInt32BigEndian(bytes); }
        string Text()
        {
            int length = Int();
            if (length < 1 || length > 256 || stream.Position + length > stream.Length) throw new IOException("Invalid road PSSG schema.");
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }
        Int(); // Native file-size field; child boundaries are checked independently.
        int attributes = Int(), elements = Int();
        if (attributes < 1 || attributes > 4096 || elements < 1 || elements > 1024) throw new IOException("Unsupported road PSSG schema size.");
        var names = new Dictionary<int, string>(); var attrNames = new Dictionary<int, string>();
        for (int i = 0; i < elements; i++)
        {
            int id = Int(); string name = Text();
            if (id < 1 || id > elements || !names.TryAdd(id, name)) throw new IOException("Invalid road PSSG element schema.");
            int count = Int(); if (count < 0 || count > attributes) throw new IOException("Invalid road PSSG attribute count.");
            for (int j = 0; j < count; j++)
            {
                int key = Int(); string value = Text();
                if (key < 1 || key > attributes || !attrNames.TryAdd(key, value)) throw new IOException("Invalid road PSSG attribute schema.");
            }
        }
        var containers = new HashSet<string>(StringComparer.Ordinal) { "PSSGDATABASE", "LIBRARY", "SHADERGROUP", "SHADERINSTANCE", "TEXTURE", "TEXTUREIMAGEBLOCK" };
        var leaves = new HashSet<string>(StringComparer.Ordinal) { "SHADERINPUTDEFINITION", "SHADERINPUT", "TEXTUREIMAGEBLOCKDATA" };
        int visited = 0;
        Node? ReadNode(long parentEnd, int depth)
        {
            if (depth > 8 || ++visited > 200000 || stream.Position + 12 > parentEnd) throw new IOException("Road PSSG structure exceeds limits.");
            int id = Int(), length = Int(); long end = stream.Position + length;
            if (length < 4 || end > parentEnd || !names.TryGetValue(id, out string? name)) throw new IOException("Invalid road PSSG element boundary.");
            if (!containers.Contains(name) && !leaves.Contains(name)) { stream.Position = end; return null; }
            int attributeBytes = Int(); long attrEnd = stream.Position + attributeBytes;
            if (attributeBytes < 0 || attrEnd > end || attributeBytes > 65536) throw new IOException("Invalid road PSSG attributes.");
            var values = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            while (stream.Position < attrEnd)
            {
                if (stream.Position + 8 > attrEnd) throw new IOException("Truncated road PSSG attribute.");
                int key = Int(), size = Int();
                if (size < 0 || size > 4096 || stream.Position + size > attrEnd || !attrNames.TryGetValue(key, out string? attr)) throw new IOException("Invalid road PSSG attribute.");
                if (!values.TryAdd(attr, reader.ReadBytes(size))) throw new IOException("Duplicate road PSSG attribute.");
            }
            long data = stream.Position; var children = new List<Node>();
            if (containers.Contains(name)) while (stream.Position < end) { var child = ReadNode(end, depth + 1); if (child != null) children.Add(child); }
            stream.Position = end;
            return new(name, values, data, checked((int)(end - data)), children);
        }
        var root = ReadNode(stream.Length, 0);
        if (root?.Name != "PSSGDATABASE" || stream.Position != stream.Length) throw new IOException("Invalid road PSSG root.");
        return root;
    }
}
