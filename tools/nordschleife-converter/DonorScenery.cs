using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Nordschleife;

// Retain referenced placement slots, but move the London scenery well below the source road.
// Optional donor crowd/cloth placements are excluded when making the fresh lab copy.
internal static class DonorScenery
{
    internal const float HiddenY = -10000;
    internal static readonly string[] OmittedRouteFiles = ["crowd_standing.bin", "crowd_standing2.bin", "crowd_standing.cns", "clothFile.bin"];

    internal static void Write(Scene scene, string donor, string output)
    {
        string Input(string relative)
        {
            string path = Files.Inside(donor, relative);
            scene.Inputs[path] = Files.Hash(path);
            return path;
        }
        var objects = Files.Pssg(Input("route_1/objects.ens"));
        var instances = Entities(objects);
        var originals = instances.ToDictionary(e => e.GetAttributeValue<string>("id"), e => Transform(e).ToArray());
        foreach (var instance in instances)
            BinaryPrimitives.WriteSingleBigEndian(Transform(instance).AsSpan(52, 4), HiddenY);
        string savedPath = Path.Combine(output, "objects.ens");
        Files.Save(objects, savedPath);
        var saved = Entities(Files.Pssg(savedPath));
        if (saved.Length != instances.Length) throw new InvalidDataException("Donor entity inventory changed.");
        foreach (var instance in saved)
        {
            var before = originals[instance.GetAttributeValue<string>("id")].ToArray();
            BinaryPrimitives.WriteSingleBigEndian(before.AsSpan(52, 4), HiddenY);
            if (!before.AsSpan().SequenceEqual(Transform(instance))) throw new InvalidDataException("Unexpected donor entity edit.");
        }
        int ornaments = Placements(Input("route_1/ornaments.xml"), Input("route_1/ornaments.bin"), output, false);
        int trees = Placements(Input("route_1/trees.xml"), Input("route_1/trees.bin"), output, true);
        var xmlReports = new List<object>();
        foreach (string name in new[] { "iwater.xml", "niwater.xml", "pfx.xml", "pfx_atmospheric.xml", "triggers_owned.xml", "triggers_effects.xml", "triggers_handbrake.xml" })
            Rewrite(Input("route_1/" + name), Path.Combine(output, name), root =>
            {
                int count = root.ChildNodes.Count;
                while (root.FirstChild is not null) root.RemoveChild(root.FirstChild);
                xmlReports.Add(new { File = name, RemovedChildren = count });
            });
        Rewrite(Input("route_1/light_placement.xml"), Path.Combine(output, "light_placement.xml"), root =>
        {
            var lights = root.SelectNodes(".//instance")!.Cast<XmlElement>().ToArray();
            foreach (var light in lights) light.ParentNode!.RemoveChild(light);
            xmlReports.Add(new { File = "light_placement.xml", RemovedInstances = lights.Length });
        });
        string shared = Path.Combine(output, "shared");
        Directory.CreateDirectory(shared);
        var terrainReports = new List<object>();
        foreach (string name in new[] { "tracksplit.pssg", "land.pssg" })
        {
            var terrain = Files.Pssg(Input(name));
            var renderers = terrain.Elements<PssgRenderInstance>().ToArray();
            foreach (var renderer in renderers) renderer.ParentElement!.RemoveChild(renderer);
            string target = Path.Combine(shared, name);
            // Visual conversion already appends local-source textures to the shared resource pack.
            if(name!="tracksplit.pssg"||!File.Exists(target))Files.Save(terrain, target);
            if (Files.Pssg(target).Elements<PssgRenderInstance>().Any()) throw new InvalidDataException("Donor common terrain remained visible.");
            terrainReports.Add(new { File = name, RemovedRenderInstances = renderers.Length, ResourceIdsRetained = true });
        }
        Rewrite(Input("ground_cover.xml"), Path.Combine(shared, "ground_cover.xml"), root =>
        {
            var system = (XmlElement?)root.SelectSingleNode("system") ?? throw new InvalidDataException("Missing ground cover limits.");
            system.SetAttribute("maxitems", "0");
            foreach (XmlElement view in root.SelectNodes("mainscene|rearviewmirror")!) view.SetAttribute("draw_distance", "0.0");
        });
        Files.Json(Path.Combine(output, "scenery.json"), new
        {
            HiddenObjects = instances.Length, HiddenOrnaments = ornaments, HiddenTrees = trees, HiddenY,
            PlacementSlotsAndIdsPreserved = true, DisabledXml = xmlReports, DisabledCommonTerrain = terrainReports, OmittedRouteFiles,
            DonorGroundCoverDisabled = true, DonorDaylightAndSkyRetained = true, RuntimeValidated = false
        });
    }
    static PssgElement[] Entities(PssgFile file) => file.Elements<PssgElement>()
        .Where(e => e.Name is "TEMPLATEBASICENTITYINSTANCE" or "TEMPLATEENTITYINSTANCE").ToArray();
    static byte[] Transform(PssgElement entity)
    {
        var value = entity.ChildElements.Single(e => e.Name == "TEMPLATETRANSFORM").Value;
        if (value.Length != 64) throw new InvalidDataException("Unsupported entity matrix.");
        return value;
    }
    internal static int Placements(string xmlPath, string binaryPath, string output, bool trees)
    {
        var doc = XDocument.Load(xmlPath);
        var bytes = File.ReadAllBytes(binaryPath);
        var original = bytes.ToArray();
        int I(int p)
        {
            if (p < 0 || (long)p + 4 > bytes.Length) throw new InvalidDataException("Placement pointer outside file.");
            return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p, 4));
        }
        var groups = doc.Root!.Elements("instanceref").ToArray();
        int header = trees ? 40 : 44, stride = trees ? 48 : 56, offset = trees ? 28 : 36;
        if ((!trees && I(0) != 0) || I(header - 8) != header || I(header - 4) != groups.Length || I(trees ? 24 : 28) != (int)doc.Root.Attribute("total_instances")!)
            throw new InvalidDataException("Unsupported placement inventory.");
        int[] packed = [0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14];
        var owned = new HashSet<int>();
        int total = 0;
        for (int g = 0; g < groups.Length; g++)
        {
            var group = groups[g];
            string name = (string)group.Attribute("filename")!;
            int record = checked(header + g * stride), text = I(record), matrices = I(record + offset + 8), count = I(record + offset + 12);
            var placements = group.Elements("instance").ToArray();
            if (text < 0 || text >= bytes.Length) throw new InvalidDataException("Invalid placement name pointer.");
            int end = Array.IndexOf(bytes, (byte)0, text);
            if (end < 0 || Encoding.UTF8.GetString(bytes, text, end - text) != name || count != placements.Length ||
                matrices < header + groups.Length * stride || (long)matrices + (long)count * 64 > bytes.Length ||
                I(record + offset) != (int)group.Attribute("max_instances")! || I(record + offset + 4) != (int)group.Attribute("offset")!)
                throw new InvalidDataException("Placement XML/binary mismatch: " + name);
            for (int i = 0; i < count; i++)
            {
                var matrix = ((string)placements[i].Attribute("transform")!).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                if (matrix.Length != 16 || matrix.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid placement transform.");
                for (int k = 0; k < packed.Length; k++)
                    if (MathF.Abs(matrix[packed[k]] - BitConverter.ToSingle(bytes, matrices + i * 64 + k * 4)) > .00005f)
                        throw new InvalidDataException("Placement transform mismatch: " + name);
                matrix[13] = HiddenY;
                placements[i].SetAttributeValue("transform", string.Join(" ", matrix.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + " ");
                int y = matrices + i * 64 + 40;
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(y, 4), HiddenY);
                for (int k = 0; k < 4; k++) if (!owned.Add(y + k)) throw new InvalidDataException("Overlapping placement transforms.");
                total++;
            }
        }
        // total_instances includes reserved dynamic slots; XML lists the static placements only.
        if (groups.Sum(g => (int)g.Attribute("max_instances")!) != (int)doc.Root.Attribute("total_instances")! || Enumerable.Range(0, bytes.Length).Any(i => bytes[i] != original[i] && !owned.Contains(i)))
            throw new InvalidDataException("Unexpected placement binary edit.");
        string nameOnly = trees ? "trees" : "ornaments";
        doc.Save(Path.Combine(output, nameOnly + ".xml"));
        using var destination = new FileStream(Path.Combine(output, nameOnly + ".bin"), FileMode.CreateNew);
        destination.Write(bytes);
        return total;
    }
    static void Rewrite(string source, string target, Action<XmlElement> update)
    {
        using var input = File.OpenRead(source);
        var file = new XmlFile(input);
        update(file.Document.DocumentElement!);
        using (var output = new FileStream(target, FileMode.CreateNew)) file.Write(output);
        using var readback = File.OpenRead(target);
        var saved = new XmlFile(readback);
        static XElement Normalize(XmlDocument document)
        {
            var root = XElement.Parse(document.DocumentElement!.OuterXml);
            // BinXml reads <root></root>, while text writers can use <root/>.
            foreach (var node in root.DescendantsAndSelf().Where(n => !n.HasElements && n.Value.Length == 0)) node.RemoveNodes();
            return root;
        }
        if (file.Type != saved.Type || !XNode.DeepEquals(Normalize(file.Document), Normalize(saved.Document)))
            throw new InvalidDataException($"Scenery XML round-trip failed ({file.Type} -> {saved.Type}): " + target);
    }
}
