using DiRT2VR.CustomTracks;

namespace DiRT2VR.Nordschleife;

// Only explicitly supported road materials; other shaders can use alpha for coverage.
public static class RoadSheen
{
    public const long MaxTextureBytes = 384L * 1024 * 1024;
    public const long MaxMaterialBytes = 512L * 1024 * 1024;
    public static bool Supports(string? layout) => TrackPacks.Nordschleife.IsLayout(layout!) || TrackPacks.Misty.IsLayout(layout!);
    public static string Target(string layout,bool highQualityAssets=false)
    {
        if (!Supports(layout)) throw new IOException("Road sheen is not supported on this track.");
        if (TrackPacks.Misty.IsLayout(layout)) return "tracks/usa/" + TrackPacks.Misty.GetLayout(layout).Folder + "/route_0/routesplit.pssg";
        return "tracks/usa/" + TrackPacks.Nordschleife.Layouts.Single(l => l.Id == layout).Folder + (highQualityAssets?"/route_0/routesplit.pssg":"/tracksplit.pssg");
    }
    public static byte[] Patch(string game, string layout, int percent)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        if (TrackPacks.Misty.IsLayout(layout)) return MistyRoadSheen.Patch(game, layout, percent);
        var installed = TrackPacks.Nordschleife.Read(game, false);
        if (installed.HighQualityAssets) return NordMaterialSheen.Patch(game, layout, percent, installed);
        string relative = Target(layout), target = SafeFiles.Inside(game, relative);
        string routePath = SafeFiles.Inside(game, relative.Replace("/tracksplit.pssg", "/route_0/routesplit.pssg", StringComparison.Ordinal));
        if (new FileInfo(target).Length > MaxTextureBytes || new FileInfo(routePath).Length > MaxTextureBytes)
            throw new IOException("Road sheen files exceed the supported size. Use track default.");
        // Registration/progress overlays may already be active. These two
        // untouched inputs must still match the installed pack's receipt.
        var receipt = TrackPacks.Nordschleife.Read(game, false);
        foreach (string path in new[] { target, routePath })
        {
            string rel = Path.GetRelativePath(game, path).Replace('\\', '/');
            var entry = receipt.Files.SingleOrDefault(f => f.Path == rel);
            if (entry is null || new FileInfo(path).Length != entry.Bytes || SafeFiles.Hash(path) != entry.Sha256)
                throw new IOException("Road sheen input is missing or changed: " + rel + ". Restore original files or rebuild the track.");
        }
        var route = RoadSheenPssg.Read(routePath);
        var groups = route.All("SHADERGROUP").Where(g => g.Text("id") == "terrain_infield.fx").ToArray();
        if (groups.Length != 1) throw new IOException("Unsupported road shader. Use track default.");
        var definitions = groups[0].Children.Where(n => n.Name == "SHADERINPUTDEFINITION").ToArray();
        var materials = route.All("SHADERINSTANCE").ToArray();
        var roads = materials.Where(m => m.Text("shaderGroup") == "#terrain_infield.fx").ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        const string prefix = "tracksplit.pssg#";
        foreach (var input in roads.SelectMany(m => m.Children).Where(n => n.Name == "SHADERINPUT"))
        {
            int index = input.Number("parameterID");
            if (index < 0 || index >= definitions.Length) throw new IOException("Unsupported road shader input.");
            if (!definitions[index].Text("name").StartsWith("TDiffuseSpecMap", StringComparison.Ordinal)) continue;
            string binding = input.Text("texture");
            if (!binding.StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("Unsupported road texture binding.");
            ids.Add(binding[prefix.Length..]);
        }
        if (roads.Length != 3 || ids.Count != 2 || materials.Except(roads)
            .Any(m => m.Children.Any(i => ids.Any(id => i.Text("texture") == prefix + id))))
            throw new IOException("Road textures are not exclusive to the supported road surfaces. Use track default.");
        var textures = RoadSheenPssg.Read(target).All("TEXTURE").Where(t => ids.Contains(t.Text("id"))).ToArray();
        if (textures.Length != ids.Count) throw new IOException("Road texture is missing.");
        var ranges = new List<(int Offset, int Length)>();
        foreach (var texture in textures)
        {
            var blocks = texture.Children.Where(n => n.Name == "TEXTUREIMAGEBLOCK").ToArray();
            if (texture.Text("texelFormat") != "dxt5" || blocks.Length != 1) throw new IOException("Unsupported road texture format. Use track default.");
            var data = blocks[0].Children.Where(n => n.Name == "TEXTUREIMAGEBLOCKDATA").ToArray();
            if (data.Length != 1 || data[0].Bytes == 0 || data[0].Bytes % 16 != 0) throw new IOException("Invalid road texture blocks.");
            ranges.Add((checked((int)data[0].Data), data[0].Bytes));
        }
        byte[] bytes = File.ReadAllBytes(target);
        byte alpha = (byte)Math.Round(percent * 255.0 / 100, MidpointRounding.AwayFromZero);
        // BC3 stores eight alpha bytes followed by eight RGB bytes. Changing
        // only those alpha bytes also preserves every mip and all PSSG metadata.
        foreach (var range in ranges)
            for (int i = range.Offset; i < range.Offset + range.Length; i += 16)
            {
                bytes.AsSpan(i, 8).Clear(); bytes[i] = bytes[i + 1] = alpha;
            }
        return bytes;
    }
}
