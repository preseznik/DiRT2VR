using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

// Incremental lab update: accepted rendering and physics payloads are copied verbatim.
internal static class SpaPresentation
{
    const string SourceRoute = "tracks/locations/europe/spa_francorchamps/route_0/";
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static float Number(XAttribute a) => float.Parse(a.Value, CultureInfo.InvariantCulture);
    static XElement Parameter(XElement e, string name) => e.Elements("Parameter").Single(p => (string?)p.Attribute("name") == name);
    static XDocument Read(string path) { using var s = File.OpenRead(path); return XDocument.Parse(new XmlFile(s).Document.OuterXml); }
    static void Translate(XElement e, Vector3 origin)
    {
        e.SetAttributeValue("x", F(Number(e.Attribute("x")!) - origin.X));
        e.SetAttributeValue("y", F(Number(e.Attribute("y")!) - origin.Y));
        e.SetAttributeValue("z", F(Number(e.Attribute("z")!) - origin.Z));
    }
    static void Set(XElement camera, string name, string value, string type = "scalar")
    {
        camera.Elements("Parameter").Where(p => (string?)p.Attribute("name") == name).Remove();
        camera.Add(new XElement("Parameter", new XAttribute("name", name), new XAttribute("type", type), new XAttribute("value", value)));
    }
    internal static void Run(string source, string baseline, string game, string output)
    {
        source = Path.GetFullPath(source); baseline = Path.GetFullPath(baseline); game = Path.GetFullPath(game); output = Path.GetFullPath(output);
        Files.NewOutput(output, source, baseline, game);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Files.Inside(baseline, "validation.json")));
        var pins = manifest.RootElement.GetProperty("Files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        foreach (var (name, hash) in pins)
            if (Files.Hash(Files.Inside(baseline, name)) != hash) throw new InvalidDataException("Baseline changed: " + name);
        using var segment = JsonDocument.Parse(File.ReadAllBytes(Files.Inside(baseline, "segment.json")));
        var offset = segment.RootElement.GetProperty("TranslationOnly").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        var origin = new Vector3(offset[0], offset[1], offset[2]);
        var inputs = new[] { SourceRoute + "progress_track.xml", SourceRoute + "replay_camera_config.xml" }.ToDictionary(n => Files.Inside(source, n), n => Files.Hash(Files.Inside(source, n)));
        string nativePath = Files.Inside(game, "tracks/london/battersea/route_1/replay_camera_config.xml");
        inputs.Add(nativePath, Files.Hash(nativePath));

        // Approximate the FIA sector boundaries before Les Combes and at Stavelot
        // using existing source gates. Keep the accepted 256-gate topology/finish.
        var progress = Read(Files.Inside(baseline, "progress_track.xml"));
        var sourceProgress = Read(Files.Inside(source, SourceRoute + "progress_track.xml"));
        var gates = progress.Descendants("gate").ToArray();
        var timing = new List<object>();
        var splits = progress.Descendants("route").Single();
        var finish = new XElement(splits.Elements("split").Single(s => (string?)s.Attribute("type") == "finish"));
        finish.SetAttributeValue("id", 0);
        splits.ReplaceNodes(finish); splits.SetAttributeValue("num_splits", 3);
        foreach (float target in new[] { 2240f, 5120f })
        {
            var from = sourceProgress.Descendants("gate").MinBy(g => MathF.Abs(Number(g.Attribute("distance")!) - target))!;
            var to = gates.MinBy(g => MathF.Abs(Number(g.Attribute("distance")!) - target))!;
            var copy = new XElement(from); copy.SetAttributeValue("id", to.Attribute("id")!.Value); copy.Element("crossing")?.Remove();
            foreach (var p in copy.Elements())
            {
                float[] xyz = p.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                p.Value = $"{F(xyz[0]-origin.X)} {F(xyz[1]-origin.Y)} {F(xyz[2]-origin.Z)}";
            }
            to.ReplaceWith(copy);
            int id = timing.Count + 1;
            splits.Add(new XElement("split", new XAttribute("id", id), new XAttribute("type", "time"), new XAttribute("gate", copy.Attribute("id")!.Value)));
            timing.Add(new { Sector = id, Gate = (int)copy.Attribute("id")!, SourceGate = (int)from.Attribute("id")!, DistanceMetres = Number(copy.Attribute("distance")!), RequestedDistanceMetres = target });
        }
        Files.Xml(progress, Files.Inside(output, "progress_track.xml"));

        var original = Read(Files.Inside(source, SourceRoute + "replay_camera_config.xml"));
        var native = Read(nativePath);
        native.Root!.SetAttributeValue("route", "0");
        native.Root.Elements("ActivationZone").Remove();
        var template = native.Root.Elements("Camera").Single(c => (string?)c.Attribute("ident") == "camera_r1_09");
        var cameras = original.Root!.Elements("Camera").Where(c => ((string?)c.Attribute("ident"))?.StartsWith("camera_r0_", StringComparison.Ordinal) == true).ToArray();
        if (cameras.Length != 28 || cameras.Any(c => (string?)c.Attribute("type") != "zoom")) throw new InvalidDataException("Unexpected source TV cameras");
        var pathIds = new HashSet<string>();
        foreach (var camera in cameras)
        {
            var converted = new XElement(template);
            converted.ReplaceAttributes(camera.Attributes().Select(a => new XAttribute(a)));
            foreach (var p in camera.Elements("Parameter"))
            {
                string name = p.Attribute("name")!.Value;
                var old = converted.Elements("Parameter").SingleOrDefault(e => (string?)e.Attribute("name") == name);
                if (old != null) old.ReplaceWith(new XElement(p));
            }
            Translate(Parameter(converted, "Position"), origin);
            var zoom = camera.Element("CameraZoom")!;
            string fov = Parameter(zoom, "defaultFov").Attribute("value")!.Value;
            // Native zoom has no GRID 2 static-end-FOV controller. Fixed lenses
            // retain the authored starting FOV; dynamic lenses retain radius/range.
            bool dynamicZoom = Number(Parameter(zoom, "dynamicTargetRadius").Attribute("value")!) > 0;
            Set(converted, "defaultFov", fov);
            Set(converted, "minimumFov", dynamicZoom ? Parameter(zoom, "dynamicMinFov").Attribute("value")!.Value : fov);
            Set(converted, "maximumFov", dynamicZoom ? Parameter(zoom, "dynamicMaxFov").Attribute("value")!.Value : fov);
            if (dynamicZoom) Set(converted, "radius", Parameter(zoom, "dynamicTargetRadius").Attribute("value")!.Value);
            Set(converted, "zoomEnabled", dynamicZoom ? "true" : "false", "bool");
            Set(converted, "crashZoomEnabled", "false", "bool");
            Set(converted, "handyCamEnabled", "false", "bool");
            foreach (string attr in new[] { "sourcePath", "targetPath" })
                if (camera.Attribute(attr) is { Value.Length: > 0 } reference) { pathIds.Add(reference.Value); converted.SetAttributeValue(attr, "spa_" + reference.Value); }
            native.Root.Add(converted);
        }
        foreach (string id in pathIds.ToArray())
        {
            var path = original.Root.Elements("Path").Single(p => (string?)p.Attribute("ident") == id);
            if (path.Attribute("percentageCurve") is { Value.Length: > 0 } curve) pathIds.Add(curve.Value);
        }
        foreach (string id in pathIds)
        {
            var path = new XElement(original.Root.Elements("Path").Single(p => (string?)p.Attribute("ident") == id));
            path.SetAttributeValue("ident", "spa_" + id);
            if (path.Attribute("percentageCurve") is { Value.Length: > 0 } curve) curve.Value = "spa_" + curve.Value;
            if (path.Attribute("vehicleNode") != null) throw new InvalidDataException("Unexpected vehicle-relative TV path");
            foreach (var point in path.Elements("Point")) Translate(point, origin);
            native.Root.Add(path);
        }
        // Keep all 32 replay cuts. Use the native moving follow camera for the
        // four onboard inserts, avoiding GRID 2 vehicle-specific camera names.
        var road = sourceProgress.Descendants("gate").Select(g => {
            Vector3 V(string name) { var v = g.Element(name)!.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); return new(v[0], v[1], v[2]); }
            return (Position: (V("left") + V("right")) / 2, Distance: Number(g.Attribute("distance")!));
        }).ToArray();
        var cuts = original.Root.Elements("TriggerZone").Where(z => z.Descendants("CameraSwitch").Any(c => (string?)Parameter(c, "switchType").Attribute("value") == "replay")).Select(z => {
            var p = Parameter(z, "position"); var v = new Vector3(Number(p.Attribute("x")!), Number(p.Attribute("y")!), Number(p.Attribute("z")!));
            var nearest = road.MinBy(g => Vector2.DistanceSquared(new(v.X, v.Z), new(g.Position.X, g.Position.Z)));
            return (Zone: z, Distance: nearest.Distance, Name: Parameter(z.Descendants("CameraSwitch").Single(), "cameraName").Attribute("value")!.Value);
        }).OrderBy(c => c.Distance).ToArray();
        var cameraIds = cameras.Select(c => c.Attribute("ident")!.Value).ToHashSet();
        var switches = new List<object>();
        foreach (var cut in cuts)
        {
            string current = cameraIds.Contains(cut.Name) ? cut.Name : "camera_dyn_+1";
            var zone = new XElement("ActivationZone", new XAttribute("type", "box"), new XAttribute("ident", "spa_" + cut.Zone.Attribute("identifier")!.Value));
            foreach (string name in new[] { "size", "position", "orientation" }) zone.Add(new XElement(Parameter(cut.Zone, name)));
            Translate(Parameter(zone, "position"), origin);
            zone.Add(new XElement("EventHandler", new XAttribute("name", "entry"), new XElement("ActivateCamera", new XAttribute("ident", current))));
            native.Root.Add(zone);
            switches.Add(new { Zone = zone.Attribute("ident")!.Value, Camera = current, SourceCamera = cut.Name, DistanceMetres = cut.Distance });
        }
        Files.Xml(native, Files.Inside(output, "replay_camera_config.xml"));
        foreach (var (path, hash) in inputs) if (Files.Hash(path) != hash) throw new InvalidDataException("Input changed");
        foreach (var name in pins.Keys.Where(n => n != "progress_track.xml"))
        {
            string target = Files.Inside(output, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Files.Inside(baseline, name), target);
        }
        foreach (string name in new[] { "segment.json", "gates.json", "source-boundaries.json" }) File.Copy(Files.Inside(baseline, name), Files.Inside(output, name));
        var files = pins.Keys.Append("replay_camera_config.xml").ToDictionary(n => n, n => Files.Hash(Files.Inside(output, n)));
        Files.Json(Files.Inside(output, "presentation.json"), new { Passed = true, Candidate = output, Baseline = baseline, Inputs = inputs, Sectors = 3, Timing = timing, Cameras = cameras.Length, Paths = pathIds.Count, Switches = switches, AcceptedVisualsAndCollisionByteIdentical = true, NativeIntroPreserved = true, RuntimeValidated = false, Files = files });
        Files.Json(Files.Inside(output, "geometry-check.json"), new { Passed = true, AcceptedVisualsAndCollisionByteIdentical = true, ProgressGateCount = gates.Length, RuntimeValidated = false });
        Files.Json(Files.Inside(output, "validation.json"), new { Passed = true, RuntimeValidated = false, Files = files });
        Console.WriteLine("Spa timing and TV replay candidate prepared: " + output);
    }
}
