using System.Xml.Linq;

internal static class TreeVertexLayouts
{
    // DiRT 2 reuses the input declaration within each tree shader. Mixed
    // vertex offsets silently read another vertex as colour/UV data.
    internal static void Validate(XDocument document)
    {
        var ids = document.Descendants().Where(e => e.Attribute("id") is not null)
            .ToDictionary(e => "#" + (string)e.Attribute("id")!, StringComparer.Ordinal);
        var layouts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var draw in document.Descendants("RENDERSTREAMINSTANCE"))
        {
            var material = ids[(string)draw.Attribute("shader")!];
            string shader = (string)material.Attribute("shaderGroup")!;
            var source = ids[(string)draw.Attribute("indices")!];
            string signature = string.Join(";", source.Elements("RENDERSTREAM").Select(stream => {
                var block = ids[(string)stream.Attribute("dataBlock")!];
                var field = block.Elements("DATABLOCKSTREAM").ElementAt((int)stream.Attribute("subStream")!);
                return string.Join(":", new[] { "renderType", "dataType", "offset", "stride" }.Select(a => (string?)field.Attribute(a)));
            }));
            if (signature.Length == 0) throw new InvalidDataException("Tree draw has no vertex streams.");
            if (layouts.TryGetValue(shader, out var previous) && previous != signature)
                throw new InvalidDataException($"Mixed tree vertex layouts for {shader}: {material.Attribute("id")!.Value}");
            layouts[shader] = signature;
        }
    }
}
