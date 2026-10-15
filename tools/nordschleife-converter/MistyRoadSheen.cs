using System.Buffers.Binary;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Nordschleife;

internal static class MistyRoadSheen
{
    internal static byte[] Patch(string game, string layout, int percent)
    {
        string relative = RoadSheen.Target(layout), path = SafeFiles.Inside(game, relative);
        var receipt = TrackPacks.Misty.Read(game, false);
        var entry = receipt.Files.SingleOrDefault(f => f.Path == relative);
        if (entry is null || entry.Bytes > RoadSheen.MaxTextureBytes || new FileInfo(path).Length != entry.Bytes || SafeFiles.Hash(path) != entry.Sha256)
            throw new IOException("Misty Loch road material is missing or changed. Restore original files or rebuild the track.");
        var route = RoadSheenPssg.Read(path);
        var group = route.All("SHADERGROUP").SingleOrDefault(g => g.Text("id") == "terrain_misty_ground.fx");
        var definitions = group?.Children.Where(n => n.Name == "SHADERINPUTDEFINITION").ToArray() ?? [];
        int parameter = Array.FindIndex(definitions, d => d.Text("name") == "MistyRoadSheen");
        if (parameter < 0) throw new IOException("Rebuild Misty Loch with this launcher to enable road sheen. Use track default with older builds.");
        var inputs = route.All("SHADERINSTANCE").Where(m => m.Text("shaderGroup") == "#terrain_misty_ground.fx")
            .SelectMany(m => m.Children.Where(i => i.Name == "SHADERINPUT" && i.Number("parameterID") == parameter)).ToArray();
        byte[] bytes = File.ReadAllBytes(path);
        var roads = new List<RoadSheenPssg.Node>();
        foreach (var input in inputs)
        {
            if (input.Text("type") != "constant" || input.Text("format") != "float4" || input.Bytes != 16)
                throw new IOException("Unsupported Misty Loch road sheen material.");
            float strength = BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(checked((int)input.Data), 4));
            if (!float.IsFinite(strength) || strength < 0) throw new IOException("Invalid Misty Loch road sheen strength.");
            if (strength > 0) roads.Add(input);
        }
        // The ground group also contains the hillside material. Never modify it.
        if (roads.Count != 1) throw new IOException("Misty Loch road material is not uniquely identified. Rebuild the track.");
        int offset = checked((int)roads[0].Data);
        BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(offset, 4), percent / 100f);
        BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(offset + 8, 4), 1); // Native Nordschleife response, retaining Misty's sun mask.
        return bytes;
    }
}
