using System.Buffers.Binary;
using System.Xml.Linq;

// Focused compatibility candidate for the two breakable log-pile models.
// Keep their combined ornament mesh and physics meshes on the same format;
// unlike the concrete barriers, their authored vertex colours are not constant.
internal static class SmelterLogMeshes
{
    internal static readonly IReadOnlySet<string> Models = new HashSet<string>(StringComparer.Ordinal) {
        "log_pile_a", "log_pile_b"
    };

    internal static object Convert(XDocument document)
    {
        var nodes = document.Descendants().Where(n => n.Attribute("id") is not null).ToDictionary(n => (string)n.Attribute("id")!);
        var roots = Models.Select(name => nodes[name + " Root"]).ToArray();
        var draws = roots.SelectMany(n => n.Descendants("RENDERSTREAMINSTANCE")).ToArray();
        var sources = draws.Select(n => ((string)n.Element("RENDERINSTANCESOURCE")!.Attribute("source")!)[1..]).ToHashSet(StringComparer.Ordinal);
        var blocks = sources.SelectMany(id => nodes[id].Elements("RENDERSTREAM")).Select(n => ((string)n.Attribute("dataBlock")!)[1..]).ToHashSet(StringComparer.Ordinal);
        if (sources.Count != 48 || blocks.Count != 48 || draws.Any(d => (string?)nodes[((string)d.Attribute("shader")!)[1..]].Attribute("shaderGroup") != "#object_simple.fx"))
            throw new InvalidDataException("Unexpected Smelter log-pile meshes.");
        if (document.Descendants("RENDERINSTANCESOURCE").Where(n => !draws.Contains(n.Parent!)).Any(n => sources.Contains(((string)n.Attribute("source")!)[1..])) ||
            document.Descendants("RENDERDATASOURCE").Where(n => !sources.Contains((string)n.Attribute("id")!)).SelectMany(n => n.Elements("RENDERSTREAM")).Any(n => blocks.Contains(((string)n.Attribute("dataBlock")!)[1..])))
            throw new InvalidDataException("Log-pile buffers are shared with unrelated scenery.");

        int vertices = 0;
        foreach (string id in blocks)
        {
            var block = nodes[id]; var fields = block.Elements("DATABLOCKSTREAM").ToArray();
            string[] semantics = ["Vertex", "Color", "ST", "ST", "Normal"];
            string[] formats = ["float3", "uint_color_argb", "half2", "half2", "half4"];
            int[] offsets = [0, 12, 16, 20, 24];
            if (fields.Length != 5 || fields.Where((f, i) => (string?)f.Attribute("renderType") != semantics[i] || (string?)f.Attribute("dataType") != formats[i] || (int)f.Attribute("offset")! != offsets[i] || (int)f.Attribute("stride")! != 32).Any())
                throw new InvalidDataException("Unexpected log-pile vertex layout.");
            var bytes = System.Convert.FromHexString(string.Concat(block.Element("DATABLOCKDATA")!.Value.Where(c => !char.IsWhiteSpace(c))));
            int count = (int)block.Attribute("elementCount")!;
            if (bytes.Length != checked(count * 32)) throw new InvalidDataException("Truncated log-pile vertices.");
            for (int i = 0; i < count; i++)
                if (BinaryPrimitives.ReadHalfBigEndian(bytes.AsSpan(i * 32 + 30)) != (Half)1)
                    throw new InvalidDataException("Unexpected log-pile normal padding.");
            vertices += count;
        }
        var subset = new XDocument(new XElement("PSSGFILE", blocks.Select(id => new XElement(nodes[id])), sources.Select(id => new XElement(nodes[id]))));
        ObjectVertexLayout.Convert(subset, keepColour: true);
        foreach (var node in subset.Root!.Elements()) nodes[(string)node.Attribute("id")!].ReplaceWith(new XElement(node));
        return new { Models = Models.Order().ToArray(), Buffers = blocks.Count, Vertices = vertices,
            PositionsUvsColoursAndIndicesPreserved = true, NormalValuesPreserved = true, RuntimeValidated = false };
    }
}
