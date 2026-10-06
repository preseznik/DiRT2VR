using System.Globalization;
using System.Numerics;

namespace DiRT2VR.Nordschleife;

internal static class SourceInventory
{
    internal static void Run(string acRoot, string output)
    {
        Files.NewOutput(output, acRoot);
        string root = Files.Inside(acRoot, "content/tracks/ks_nordschleife");
        string ini = Files.Inside(root, "models_nordschleife.ini");
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ini] = Files.Hash(ini) };
        var models = Scene.Ini(File.ReadAllText(ini));
        var textures = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        var meshes = new List<object>();
        var files = new List<object>();
        var textureModels = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        long visualTriangles = 0, physicalTriangles = 0, dynamicTriangles = 0;
        var unmapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (section, settings) in models.Where(m => m.Key.StartsWith("MODEL_", StringComparison.Ordinal) || m.Key.StartsWith("DYNAMIC_OBJECT_", StringComparison.Ordinal)))
        {
            bool dynamic = section.StartsWith("DYNAMIC_OBJECT_", StringComparison.Ordinal);
            string file = settings["FILE"], path = Files.Inside(root, file);
            inputs[path] = Files.Hash(path);
            var placement = Matrix4x4.Identity;
            if (!dynamic)
            {
                if (settings["ROTATION"] != "0,0,0") throw new InvalidDataException("Unexpected model rotation: " + section);
                var p = settings["POSITION"].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                if (p.Length != 3 || p.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid source model position.");
                placement = Matrix4x4.CreateTranslation(p[0], p[1], p[2]);
            }
            int meshCount = 0;
            Console.WriteLine("Inventory " + file);
            Kn5.Read(path, placement, textures, mesh =>
            {
                meshCount++;
                int triangles = mesh.Indices.Length / 3;
                if (dynamic) dynamicTriangles += triangles;
                else
                {
                    if (mesh.Visible) visualTriangles += triangles;
                    if (mesh.Active && mesh.Physical) physicalTriangles += triangles;
                }
                string? surface = null;
                if (mesh.Active && mesh.Physical)
                {
                    try { surface = Scene.Surface(mesh.Name); }
                    catch (InvalidDataException) { unmapped.Add(mesh.Name); }
                }
                string? target = null;
                try { target = Visuals.Target(mesh.Material); }
                catch (InvalidDataException) { unmapped.Add("shader:" + mesh.Material.Shader); }
                foreach (string name in mesh.Material.Textures.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!textureModels.TryGetValue(name, out var references)) textureModels[name] = references = new(StringComparer.Ordinal);
                    references.Add(file);
                }
                meshes.Add(new
                {
                    Model = section, File = file, Role = dynamic ? "dynamic" : "static", mesh.SourceNode, mesh.Name,
                    mesh.Active, mesh.Visible, mesh.Renderable, mesh.Physical, GameMarker=Scene.IsGameMarker(mesh.Name), Triangles = triangles, Vertices = mesh.Positions.Length,
                    BoundsMin = mesh.Positions.Length == 0 ? null : Coordinates(mesh.Positions.Aggregate(Vector3.Min)),
                    BoundsMax = mesh.Positions.Length == 0 ? null : Coordinates(mesh.Positions.Aggregate(Vector3.Max)),
                    mesh.LodIn, mesh.LodOut, Material = mesh.Material.Id, mesh.Material.Shader,
                    TargetShader = target, CollisionSurface = surface, Textures = mesh.Material.Textures,
                    Properties = mesh.Material.Properties
                });
            }, includeInactive: true);
            files.Add(new { Section = section, File = file, Role = dynamic ? "dynamic" : "static", Meshes = meshCount, Settings = settings });
        }
        var missing = textureModels.Keys.Where(name => !textures.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0) throw new InvalidDataException("Missing source textures: " + string.Join(", ", missing));
        foreach (var (path, hash) in inputs) if (Files.Hash(path) != hash) throw new IOException("Source changed during inventory: " + path);
        var payloads = textures.Values.DistinctBy(t => t.Hash).ToArray();
        Files.Json(Path.Combine(output, "scene.json"), new
        {
            Schema = 1, Layout = "ks_nordschleife/nordschleife", StaticModels = models.Keys.Count(k => k.StartsWith("MODEL_", StringComparison.Ordinal)),
            DynamicModels = models.Keys.Count(k => k.StartsWith("DYNAMIC_OBJECT_", StringComparison.Ordinal)),
            VisualTriangles = visualTriangles, PhysicalTriangles = physicalTriangles, DynamicTriangles = dynamicTriangles,
            UniqueTexturePayloads = payloads.Length, TexturePayloadBytes = payloads.Sum(t => (long)t.Dds.Length),
            Unmapped = unmapped.Order(StringComparer.Ordinal).ToArray(), Files = files, Meshes = meshes,
            Textures = textures.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => new { Name = t.Key, t.Value.Hash, Bytes = t.Value.Dds.Length, ReferencedBy = textureModels.GetValueOrDefault(t.Key)?.Order(StringComparer.Ordinal).ToArray() ?? [] }),
            RuntimeValidated = false, DynamicPlacementsConverted = false
        });
        Files.Json(Path.Combine(output, "inputs.json"), inputs);
        Console.WriteLine($"Inventory: {visualTriangles:N0} static visual, {physicalTriangles:N0} physical, {dynamicTriangles:N0} dynamic triangles; {payloads.Length} texture payloads. No scene cropping.");
    }

    static float[] Coordinates(Vector3 value) => [value.X, value.Y, value.Z];
}
