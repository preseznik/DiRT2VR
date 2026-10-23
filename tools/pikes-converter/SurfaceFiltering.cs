using System.Security.Cryptography;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Retain the accepted shaders, DDS mip chains and geometry while correcting sampling.
internal static class SurfaceFiltering
{
    internal static void ApplyToNewBuild(string output)
    {
        // Operate only inside the newly created conversion output.
        string target = Path.Combine(output, "surface-filtering.pssg");
        var proof = Filter(output, target);
        File.Move(target, Files.Inside(output, "shared/tracksplit.pssg"), true);
        Files.Json(Path.Combine(output, "surface-filtering.json"), proof);
    }
    static object Filter(string candidate, string target)
    {
        var route = Files.Pssg(Files.Inside(candidate, "routesplit.pssg"));
        var uses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var shader in route.Elements<PssgShaderInstance>())
        {
            var group = shader.GetShaderGroup();
            if (!group.Id.StartsWith("terrain_", StringComparison.Ordinal) &&
                !group.Id.StartsWith("pikes_bank_", StringComparison.Ordinal) &&
                !group.Id.StartsWith("pikes_terrain_", StringComparison.Ordinal)) continue;
            var root = new XElement("root");
            group.WriteXml(root); shader.WriteXml(root);
            var definitions = root.Element("SHADERGROUP")!.Elements("SHADERINPUTDEFINITION").ToArray();
            foreach (var input in root.Element("SHADERINSTANCE")!.Elements("SHADERINPUT"))
            {
                string name = (string)definitions[(int)input.Attribute("parameterID")!].Attribute("name")!;
                string? reference = (string?)input.Attribute("texture");
                if (!name.StartsWith("TDiffuse", StringComparison.Ordinal) || reference is null) continue;
                if (!reference.StartsWith("tracksplit.pssg#nord_tex_", StringComparison.Ordinal)) continue;
                string id = reference[(reference.IndexOf('#') + 1)..];
                if (!uses.TryGetValue(id, out var bindings)) uses.Add(id, bindings = []);
                bindings.Add(group.Id + "/" + name);
            }
        }
        string original = Files.Inside(candidate, "shared/tracksplit.pssg");
        var file = Files.Pssg(original);
        string[] Payloads(PssgFile p) => p.Elements<PssgElement>().Where(e => e.Value.Length > 0)
            .Select(e => e.Name + ":" + Convert.ToHexString(SHA256.HashData(e.Value))).ToArray();
        Dictionary<string,string> Attributes(PssgTexture texture)
        {
            var root = new XElement("root"); texture.WriteXml(root);
            return root.Element("TEXTURE")!.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
        }
        var before = Payloads(file);
        var samplers = file.Elements<PssgTexture>().ToDictionary(t => t.Id, Attributes);
        var changed = new List<object>(); var skipped = new List<object>();
        foreach (var (id, bindings) in uses.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var texture = file.GetObject<PssgTexture>(id.AsMemory());
            if (texture.MinFilter != 5)
            {
                skipped.Add(new { Texture = id, Reason = "Existing minification is not anisotropic", Sampler = samplers[id] });
                continue;
            }
            if (texture.MaxAnisotropy == 16 && texture.MagFilter == 5) continue;
            changed.Add(new { Texture = id, Bindings = bindings.Order().ToArray(), Before = samplers[id] });
            texture.MaxAnisotropy = 16; texture.MagFilter = 5;
        }
        if (changed.Count == 0) throw new InvalidDataException("No terrain diffuse samplers need this comparison.");
        Files.Save(file, target);
        var saved = Files.Pssg(target);
        if (!before.SequenceEqual(Payloads(saved))) throw new InvalidDataException("Texture or geometry payload changed.");
        foreach (var texture in saved.Elements<PssgTexture>())
        {
            var expected = new Dictionary<string,string>(samplers[texture.Id]);
            if (uses.ContainsKey(texture.Id) && file.GetObject<PssgTexture>(texture.Id.AsMemory()).MinFilter == 5)
            {
                // PSSG stores anisotropy as a float; use its canonical XML spelling.
                var adapted = Attributes(file.GetObject<PssgTexture>(texture.Id.AsMemory()));
                expected["maxAnisotropy"] = adapted["maxAnisotropy"]; expected["magFilter"] = adapted["magFilter"];
                if (texture.MaxAnisotropy != 16 || texture.MagFilter != 5)
                    throw new InvalidDataException("Sampler did not survive serialization: " + texture.Id);
            }
            if (!expected.OrderBy(p => p.Key).SequenceEqual(Attributes(texture).OrderBy(p => p.Key)))
                throw new InvalidDataException("Unexpected texture attribute change: " + texture.Id +
                    " expected=" + System.Text.Json.JsonSerializer.Serialize(expected) +
                    " actual=" + System.Text.Json.JsonSerializer.Serialize(Attributes(texture)));
        }
        string hash = Files.Hash(target);
        var proof = new {
            SourceCandidate = Path.GetFullPath(candidate), OriginalHash = Files.Hash(original), NewHash = hash,
            ChangedTextureCount = changed.Count, Changed = changed, Skipped = skipped,
            MaxAnisotropy = 16, MagFilter = 5, MinFilterUnchanged = true,
            AllPayloadDataBlocksUnchanged = true, PayloadDataBlocks = before.Length,
            OtherTextureAttributesUnchanged = true, RenderingProbeOnly = true, RuntimeValidated = false };
        Console.WriteLine($"Terrain diffuse comparison: {changed.Count} samplers; {before.Length} payload blocks unchanged.");
        return proof;
    }
}
