using System.Xml.Linq;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class ShaderTemplates
{
    // Production builds supply the pinned stock donor list. Lab audit commands
    // retain discovery, excluding custom track folders from their donor search.
    internal static string[]? Donors;
    internal static Dictionary<string, XElement> Load(string game, Dictionary<string, string>? fingerprints = null)
    {
        var templates = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var names = new HashSet<string> { "routesplit.pssg", "tracksplit.pssg", "objects.pssg", "trees.pssg", "sky.pssg", "ground_cover.pssg", "land.pssg", "iwater.pssg", "niwater.pssg", "patchup_ot.pssg" };
        var paths = Donors?.Select(p => Path.Combine(game, p)) ?? Directory.EnumerateFiles(Path.Combine(game, "tracks"), "*.pssg", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s.StartsWith("d2vr_", StringComparison.OrdinalIgnoreCase)));
        foreach (var path in paths.Where(p => names.Contains(Path.GetFileName(p))).Order(StringComparer.Ordinal))
        {
            using var stream = PortFiles.OpenRead(path);
            var file = PssgFile.Open(stream);
            bool used = false;
            foreach (var group in file.Elements<PssgElement>().Where(e => e.Name == "SHADERGROUP"))
            {
                var id = group.GetAttributeValue<string>("id");
                if (templates.ContainsKey(id)) continue;
                var root = new XElement("root"); group.WriteXml(root);
                templates.Add(id, root.Elements().Single()); used = true;
            }
            if (used && fingerprints is not null) fingerprints[path] = PortFiles.Hash(path);
        }
        return templates;
    }
}
