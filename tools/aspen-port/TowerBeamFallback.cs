using System.Xml.Linq;
using System.Buffers.Binary;

// Only the twelve elevated ski-hillside glows opposite the reported Sprint bend.
// Keep the physical rigs, emissive lamp faces, light definitions and mesh resources.
internal static class TowerBeamFallback
{
    // Baked beam X/Z bounds centres identify the source placements independently
    // of generated mesh IDs. Source fingerprints pin the authored coordinates.
    static readonly (float X, float Z)[] Hillside = [
        (-179.682f,51.870f), (-160.663f,74.144f), (-221.690f,125.538f),
        (-240.818f,102.755f), (-292.748f,246.359f), (8.958f,226.673f),
        (-80.114f,264.605f), (-161.054f,229.083f), (-88.885f,203.054f),
        (-213.488f,232.301f), (-54.659f,154.043f), (-137.198f,110.902f)
    ];

    internal static string[] Disable(XDocument objects, XDocument land)
    {
        var rig = objects.Descendants("ROOTNODE").Single(n => (string?)n.Attribute("id") == "xgames_light_rig_a Root");
        var groups = objects.Descendants("SHADERINSTANCE").ToDictionary(n => "#" + (string)n.Attribute("id")!, n => (string)n.Attribute("shaderGroup")!);
        var materials = rig.Descendants("RENDERSTREAMINSTANCE")
            .Select(n => (string)n.Attribute("shader")!).Where(m => groups[m] == "#volumetrics.fx")
            .Select(m => "#aspen_static_" + m[1..]).ToHashSet();
        if (materials.Count != 1) throw new InvalidDataException("Unexpected Aspen tower beam materials.");
        var nodes = land.Descendants().Where(n => n.Attribute("id") is not null).ToDictionary(n => (string)n.Attribute("id")!);
        (float X, float Z) Centre(XElement draw)
        {
            var mesh = nodes[((string)draw.Element("RENDERINSTANCESOURCE")!.Attribute("source")!)[1..]];
            var stream = mesh.Elements("RENDERSTREAM").Select(s => (Block:nodes[((string)s.Attribute("dataBlock")!)[1..]],Index:(int)s.Attribute("subStream")!))
                .Single(s => (string?)s.Block.Elements("DATABLOCKSTREAM").ElementAt(s.Index).Attribute("renderType") == "Vertex");
            var field = stream.Block.Elements("DATABLOCKSTREAM").ElementAt(stream.Index);
            if ((string?)field.Attribute("dataType") != "float3") throw new InvalidDataException("Unsupported beam positions.");
            int count = (int)stream.Block.Attribute("elementCount")!, stride = (int)field.Attribute("stride")!, offset = (int)field.Attribute("offset")!;
            var bytes = System.Convert.FromHexString(string.Concat(stream.Block.Element("DATABLOCKDATA")!.Value.Where(c => !char.IsWhiteSpace(c))));
            if (count < 1 || stride < 12 || offset < 0 || (long)(count-1)*stride+offset+12 > bytes.Length) throw new InvalidDataException("Invalid beam position buffer.");
            float minX=float.MaxValue, minZ=float.MaxValue, maxX=float.MinValue, maxZ=float.MinValue;
            for(int i=0;i<count;i++)
            {
                float x=BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(i*stride+offset)), z=BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(i*stride+offset+8));
                if(!float.IsFinite(x)||!float.IsFinite(z)) throw new InvalidDataException("Nonfinite beam position.");
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
            }
            return ((minX+maxX)/2,(minZ+maxZ)/2);
        }
        var candidates = land.Descendants("RENDERSTREAMINSTANCE").Where(n => materials.Contains((string)n.Attribute("shader")!)).Select(n => (Draw:n,Centre:Centre(n))).ToArray();
        var draws = Hillside.Select(anchor => candidates.Single(n => Math.Abs(n.Centre.X-anchor.X)<.02f && Math.Abs(n.Centre.Z-anchor.Z)<.02f).Draw).ToArray();
        if (draws.Distinct().Count() != Hillside.Length || draws.Any(n => !((string)n.Parent!.Attribute("id")!).StartsWith("NONLOD_", StringComparison.Ordinal)))
            throw new InvalidDataException("Expected persistent Aspen tower beam draws.");
        var ids = draws.Select(n => (string)n.Attribute("id")!).ToArray();
        foreach (var draw in draws) draw.Remove();
        return ids;
    }

    internal static object Convert(string track)
    {
        string path = Path.Combine(track, "land.pssg");
        var before = PortFiles.ReadPssg(path); var after = new XDocument(before);
        var disabled = Disable(PortFiles.ReadPssg(Path.Combine(track, "objects.pssg")), after);
        PortFiles.WritePssg(after, path + ".tmp");
        var saved = PortFiles.ReadPssg(path + ".tmp");
        ObjectVertexLayout.Verify(before, saved);
        if (!XNode.DeepEquals(after, saved)) throw new InvalidDataException("Tower beam fallback changed unrelated scene data during serialization.");
        File.Move(path + ".tmp", path, true);
        return new { DisabledDecorativeTowerBeams = disabled, TowersLampFacesAndOtherDrawsPreserved = true, MeshResourcesPreserved = true };
    }
}
