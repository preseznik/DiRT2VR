using System.Buffers.Binary;
using System.Xml.Linq;

// County Loop compatibility candidate: use the vertex declaration found on
// native DiRT 2 concrete barriers. Do not normalize unrelated scenery or Aspen.
internal static class SmelterBarrierMeshes
{
    internal static readonly IReadOnlySet<string> Models = new HashSet<string>(StringComparer.Ordinal) {
        "core_barr_blockconcrete_d~0", "core_barr_blockconcrete_d~1"
    };

    internal static object Convert(XDocument document)
    {
        var nodes = document.Descendants().Where(n => n.Attribute("id") is not null).ToDictionary(n => (string)n.Attribute("id")!);
        var roots = Models.Select(name => nodes[name + " Root"]).ToArray();
        var draws = roots.SelectMany(n => n.Descendants("RENDERSTREAMINSTANCE")).ToArray();
        var sources = draws.Select(n => ((string)n.Element("RENDERINSTANCESOURCE")!.Attribute("source")!)[1..]).ToHashSet(StringComparer.Ordinal);
        var blocks = sources.SelectMany(id => nodes[id].Elements("RENDERSTREAM")).Select(n => ((string)n.Attribute("dataBlock")!)[1..]).ToHashSet(StringComparer.Ordinal);
        if (sources.Count != 6 || blocks.Count != 6 || draws.Any(d => (string?)nodes[((string)d.Attribute("shader")!)[1..]].Attribute("shaderGroup") != "#object_simple.fx"))
            throw new InvalidDataException("Unexpected County Loop concrete barrier meshes.");
        if (document.Descendants("RENDERSTREAMINSTANCE").Except(draws).Any(d => sources.Contains(((string)d.Element("RENDERINSTANCESOURCE")!.Attribute("source")!)[1..])) ||
            document.Descendants("RENDERDATASOURCE").Where(n => !sources.Contains((string)n.Attribute("id")!)).SelectMany(n => n.Elements("RENDERSTREAM")).Any(n => blocks.Contains(((string)n.Attribute("dataBlock")!)[1..])))
            throw new InvalidDataException("Concrete barrier buffers are shared with unrelated scenery.");

        int vertices = 0;
        foreach (string id in blocks)
        {
            var block = nodes[id]; var fields = block.Elements("DATABLOCKSTREAM").ToArray();
            string[] semantics = ["Vertex", "Color", "ST", "ST", "Normal"];
            string[] formats = ["float3", "uint_color_argb", "half2", "half2", "half4"];
            int[] offsets = [0, 12, 16, 20, 24];
            if (fields.Length != 5 || fields.Where((f, i) => (string?)f.Attribute("renderType") != semantics[i] || (string?)f.Attribute("dataType") != formats[i] || (int)f.Attribute("offset")! != offsets[i] || (int)f.Attribute("stride")! != 32).Any())
                throw new InvalidDataException("Unexpected concrete barrier vertex layout.");
            var bytes = System.Convert.FromHexString(string.Concat(block.Element("DATABLOCKDATA")!.Value.Where(c => !char.IsWhiteSpace(c))));
            int count = (int)block.Attribute("elementCount")!;
            if (bytes.Length != checked(count * 32)) throw new InvalidDataException("Truncated concrete barrier vertices.");
            for (int i = 0; i < count; i++)
                // Match the native declaration, which has no per-vertex colour.
                // Reject nonconstant source colours instead of dropping authored variation.
                if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i * 32 + 12)) != 0xff000000 || BinaryPrimitives.ReadHalfBigEndian(bytes.AsSpan(i * 32 + 30)) != (Half)1)
                    throw new InvalidDataException("Unexpected concrete barrier colour or normal padding.");
            vertices += count;
        }
        var subset = new XDocument(new XElement("PSSGFILE", blocks.Select(id => new XElement(nodes[id])), sources.Select(id => new XElement(nodes[id]))));
        ObjectVertexLayout.Convert(subset, keepColour: false);
        foreach (var node in subset.Root!.Elements()) nodes[(string)node.Attribute("id")!].ReplaceWith(new XElement(node));
        return new { Models = Models.Order().ToArray(), Buffers = blocks.Count, Vertices = vertices, NativeStride = 32,
            PositionsUvsAndIndicesPreserved = true, NormalValuesPreserved = true, RemovedConstantColour = "FF000000", RuntimeValidated = false };
    }
}
