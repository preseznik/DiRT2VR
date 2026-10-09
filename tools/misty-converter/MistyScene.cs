using System.Numerics;
using DiRT2VR.Nordschleife;

internal static class MistyScene
{
    internal static Scene Read(string acRoot, string game, string output, float length, bool fullCourse=false)
    {
        if (!float.IsFinite(length) || length is < 500 or > 1000) throw new ArgumentException("Desktop prototype length must be 500–1000 m.");
        var scene = new Scene { FullCourse=fullCourse };
        string root = Files.Inside(acRoot, "content/tracks/rt_misty_loch");
        string Input(string relative)
        {
            string path = Files.Inside(root, relative); scene.Inputs[path] = Files.Hash(path); return path;
        }
        // CSP configuration is inventoried, never executed. Supported effects are reconstructed locally.
        foreach (string file in new[] { "normal/data/surfaces.ini", "extension/ext_config.ini", "readme.txt", "ui/normal/ui_track.json" }) Input(file);
        var models = Scene.Ini(File.ReadAllText(Input("models_normal.ini")));
        using (var r = new BinaryReader(File.OpenRead(Input("normal/ai/fast_lane.ai"))))
        {
            if (r.ReadInt32() != 7) throw new InvalidDataException("Unsupported AC spline version.");
            int n = r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
            if (n is < 10 or > 100000) throw new InvalidDataException("Invalid spline count.");
            var points = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                points[i] = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); r.ReadSingle(); r.ReadInt32();
                if (!Kn5.Finite(points[i])) throw new InvalidDataException("Invalid spline position.");
            }
            if (r.ReadInt32() != n) throw new InvalidDataException("Missing source road widths.");
            var sides = new (float Left, float Right)[n];
            for (int i = 0; i < n; i++)
            {
                var fields = new float[18]; for (int j = 0; j < 18; j++) fields[j] = r.ReadSingle();
                sides[i] = (fields[5], fields[6]);
                if (!float.IsFinite(fields[5]) || !float.IsFinite(fields[6]) || fields[5] <= 0 || fields[6] <= 0)
                    throw new InvalidDataException("Invalid source road width.");
            }
            scene.Origin = points[0] - Vector3.UnitY * 8;
            var gates = new List<Gate>(); float distance = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) distance += Vector3.Distance(points[i - 1], points[i]);
                if (!fullCourse && distance > length + 90) break;
                if (i > 0 && distance - gates[^1].Distance < 5) continue;
                var tangent = Vector3.Normalize(points[(i + 1) % n] - points[(i + n - 1) % n]);
                if (!Kn5.Finite(tangent)) throw new InvalidDataException("Invalid spline tangent.");
                gates.Add(new(points[i] - scene.Origin, tangent, sides[i].Left, sides[i].Right, distance));
            }
            scene.Gates = (fullCourse?gates:gates.Where(g => g.Distance <= length)).ToArray();
            scene.Length = fullCourse?distance+Vector3.Distance(points[^1],points[0]):scene.Gates[^1].Distance;
            scene.RunoutGates = fullCourse?[]:gates.Where(g => g.Distance > scene.Length).ToArray();
            if (!fullCourse && (gates.Count + 1 > ProgressGates.NativeLimit || scene.RunoutGates.Length < 10))
                throw new InvalidDataException("Segment exceeds crossing-plane budget or lacks runout.");
        }
        var corridor = scene.Gates.Concat(scene.RunoutGates).Select(g => g.Position)
            .Append(scene.Gates[0].Position - scene.Gates[0].Tangent * 30).ToArray();
        var cells = corridor.GroupBy(Scene.Tile).ToDictionary(g => g.Key, g => g.ToArray());
        bool Near(Vector3 p, float radius)
        {
            var tile = Scene.Tile(p); int reach = (int)MathF.Ceiling(radius / Scene.TileSize);
            for (int x = -reach; x <= reach; x++) for (int z = -reach; z <= reach; z++)
                if (cells.TryGetValue(new(tile.X + x, tile.Z + z), out var samples))
                    foreach (var q in samples)
                        if ((p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z) <= radius*radius && MathF.Abs(p.Y-q.Y)<180) return true;
            return false;
        }
        var adapter = new MistyMaterials(scene,game);
        var attributes = new MistyMeshAttributes();
        foreach (var (section, values) in models.Where(x => x.Key.StartsWith("MODEL_", StringComparison.Ordinal)))
        {
            if (values["POSITION"] != "0,0,0" || values["ROTATION"] != "0,0,0") throw new InvalidDataException("Source placement changed: " + section);
            string file = values["FILE"]; Console.WriteLine("Read " + file);
            Kn5.Read(Input(file), Matrix4x4.CreateTranslation(-scene.Origin), scene.Textures, mesh =>
            {
                bool helper = mesh.Name.StartsWith("AC_", StringComparison.Ordinal);
                var selected = new List<ushort>(); int collision = 0;
                for (int i = 0; i < mesh.Indices.Length; i += 3)
                {
                    var a=mesh.Positions[mesh.Indices[i]]; var b=mesh.Positions[mesh.Indices[i+1]]; var c=mesh.Positions[mesh.Indices[i+2]];
                    bool In(float radius) => Near((a+b+c)/3,radius)||Near(a,radius)||Near(b,radius)||Near(c,radius);
                    if (mesh.Active && mesh.Physical && (fullCourse || In(120)))
                    {
                        string surface = Surface(mesh.Name); scene.Collision.Add(new(a,b,c,surface)); collision++;
                        scene.CollisionMappings[mesh.Name] = surface; scene.Surfaces[surface] = scene.Surfaces.GetValueOrDefault(surface)+1;
                    }
                    // Preserve the complete lake plane where it bounds the crop; retain local scenery within 250 m.
                    if (mesh.Visible && !helper && (fullCourse || mesh.Material.Name == "lake1" || In(250))) selected.AddRange([mesh.Indices[i],mesh.Indices[i+1],mesh.Indices[i+2]]);
                }
                if (selected.Count > 0)
                {
                    var adapted=mesh with { Material=adapter.Convert(mesh.Material) };
                    adapted=attributes.Apply(adapted);
                    scene.Visuals.Add(Scene.Compact(adapted,selected));
                }
                scene.SourceMeshes.Add(new {File=file,mesh.SourceNode,mesh.Name,mesh.Active,mesh.Visible,mesh.Physical,Helper=helper,SourceTriangles=mesh.Indices.Length/3,VisualTriangles=selected.Count/3,CollisionTriangles=collision});
            },includeInactive:true);
        }
        MistyGrass.Add(scene,acRoot,game,output);
        // Consolidate compatible static meshes before spatial tiling, preserving all selected faces and attributes.
        var batches=VisualBatches.Combine(scene.Visuals).ToArray(); scene.Visuals.Clear(); scene.Visuals.AddRange(batches);
        if (scene.Visuals.Count==0 || scene.Collision.Count==0) throw new InvalidDataException("Empty prototype.");
        Files.Json(Path.Combine(output,"source-adapter.json"),new {Source="rt_misty_loch/normal",ImportedModels=10,DynamicModelsOmitted=new[]{"seagulls.kn5"},SceneryRadiusMetres=fullCourse?0:250,CollisionRadiusMetres=fullCourse?0:120,SourceMetresAndHandednessRetained=true,TranslationOnly=true,FullCourse=fullCourse,BoatMotion="Native rigid float; source sailing paths unsupported",WaterMotion="Native timed foam and lake water",HelicopterMotion="Static",CspEffectsFullyReproduced=false,MaterialMappings=adapter.Report,RuntimeValidated=false});
        Files.Json(Path.Combine(output,"source-scene.json"),scene.SourceMeshes);
        return scene;
    }
    internal static string Surface(string name)
    {
        string key=name.TrimStart("0123456789".ToCharArray()).ToUpperInvariant();
        if(key.StartsWith("KERB",StringComparison.Ordinal))return "CON+";
        if(key.StartsWith("PIT",StringComparison.Ordinal))return "RDT+";
        return Scene.Surface(name);
    }
}
