using System.Xml.Linq;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.TrackQuadTree;

internal static class PortBuild
{
    internal static string? SchemaPath;
    internal static HashSet<string>? SourceFiles;
    internal static Action<string>? Progress;
    internal static void Run(string d3, string d2, string output, AspenLayout? layout = null, AspenCondition? condition = null)
    {
        layout ??= AspenLayout.Lakeside;
        condition ??= AspenCondition.Default(layout);
        PortFiles.NewOutput(output, d3, d2);
        var source = Path.Combine(d3, "tracks/locations/usa/aspen");
        var sourceRoute = Path.Combine(source, layout.SourceRoute);
        var donor = Path.Combine(d2, "tracks/london/battersea");
        var track = Path.Combine(output, "track");
        var route = Path.Combine(track, "route_0");
        Directory.CreateDirectory(route);
        var inputs = new Dictionary<string, string>();
        Progress?.Invoke("Reading stock shader definitions");
        var templates = ShaderTemplates.Load(d2, inputs);
        var shaders = new List<object>(); var copies = new List<object>();
        var placements = new List<object>();
        // DiRT 2 groups renderer slots by model; the ENS dynamic instances share
        // this slot space with ornaments.bin and must be remapped together.
        var entityPath = Path.Combine(sourceRoute, "objects.ens");
        var ornamentPath = Path.Combine(sourceRoute, "ornaments.bin");
        inputs[entityPath] = PortFiles.Hash(entityPath); inputs[ornamentPath] = PortFiles.Hash(ornamentPath);
        var routeEntities = PortFiles.ReadPssg(entityPath);
        PortFiles.NoLinks(ornamentPath);
        var ornaments = Placements.Convert(File.ReadAllBytes(ornamentPath), false, placements, routeEntities);
        void Copy(string path, string target)
        {
            PortFiles.NoLinks(path);
            inputs[path] = PortFiles.Hash(path);
            if (Path.GetExtension(path) is ".pssg" or ".ens")
            {
                var doc = path == entityPath ? routeEntities : PortFiles.ReadPssg(path);
                bool entities = Path.GetExtension(path) == ".ens" || Path.GetFileName(path) == "objecttypes.pssg";
                if (entities) Entities.Convert(doc, Path.GetExtension(path) != ".ens");
                ShaderConversion.Convert(doc, templates, Path.GetRelativePath(output, target), shaders);
                if (Path.GetFileName(path) == "trees.pssg") TreeVertexLayouts.Validate(doc);
                PortFiles.WritePssg(doc, target);
                // Independently decode the saved binary, including text-format source ENS files.
                var saved = PortFiles.ReadPssg(target);
                if (entities) Entities.Verify(doc, saved);
                if (Path.GetFileName(path) == "trees.pssg") TreeVertexLayouts.Validate(saved);
            }
            else if (path == ornamentPath) File.WriteAllBytes(target, ornaments);
            else if (Path.GetFileName(path) is "trees.bin" or "ornaments.bin")
                File.WriteAllBytes(target, Placements.Convert(File.ReadAllBytes(path), Path.GetFileName(path) == "trees.bin", placements));
            else if (Path.GetFileName(path) == "ai_track.xml")
            {
                var data = PortFiles.ReadXml(path);
                AiTrack.Convert(data);
                PortFiles.WriteXml(data, target, EgoEngineLibrary.Xml.XmlType.BinXml);
                if (!XNode.DeepEquals(data, PortFiles.ReadXml(target)))
                    throw new InvalidDataException("AI track data changed during serialization.");
            }
            else if (Path.GetFileName(path) == "replay_camera_config.xml")
                PortFiles.WriteXml(ReplayCameras.Convert(PortFiles.ReadXml(path)), target, EgoEngineLibrary.Xml.XmlType.BinXml);
            else if (Path.GetFileName(path) == "route_overrides.xml" && (layout == AspenLayout.SnowmassSprint || layout.PracticeTest))
            {
                var settings = PortFiles.ReadXml(path);
                TerrainRange.Convert(settings, layout.PracticeTest);
                PortFiles.WriteXml(settings, target, EgoEngineLibrary.Xml.XmlType.BinXml);
                if (!XNode.DeepEquals(settings, PortFiles.ReadXml(target)))
                    throw new InvalidDataException("Terrain range settings changed during serialization.");
            }
            else if (Path.GetFileName(path) is "cloth.xml" or "lod_overrides.xml" or "ornament_attributes.xml" or "tree_attributes.xml")
                PortFiles.WriteXml(PortFiles.ReadXml(path), target, EgoEngineLibrary.Xml.XmlType.BinXml);
            else PortFiles.CopyNew(path, target);
            copies.Add(new { Source = path, Target = Path.GetRelativePath(output, target) });
        }
        foreach (var root in new[] { source, sourceRoute })
        foreach (var path in Directory.EnumerateFiles(root).Order(StringComparer.Ordinal))
        {
            if (SourceFiles is not null && !SourceFiles.Contains(Path.GetFullPath(path))) continue;
            var name = Path.GetFileName(path);
            if (!condition.Include(name) || name is "snow.pssg" or "snow.xml" or "track.jpk" or "crowd_standing2.bin") continue;
            Copy(path, Path.Combine(root == source ? track : route, name));
        }
        // DiRT 2 loads unsuffixed resources; select one internally consistent set.
        foreach (var pair in new[] { ($"{layout.SourceRoute}/sky_{condition.Suffix}.pssg", "route_0/sky.pssg"), ($"{layout.SourceRoute}/sky_{condition.Suffix}.pssg", "sky.pssg"), ($"lighting_{condition.Suffix}.xml", "lighting.xml"), ($"bouncemap_{condition.Suffix}.clm", "baked_lights.clm") })
            Copy(Path.Combine(source, pair.Item1), Path.Combine(track, pair.Item2));
        // No authored sun-shadow CLM exists at night. Do not import daylight shadows.
        if (!condition.Night) Copy(Path.Combine(sourceRoute,$"shadow_map_{condition.Suffix}.clm"),Path.Combine(route,"route.clm"));
        foreach (var pair in new[] { ("sponsor_sparco.pssg", "sponsor_pack_a.pssg"), ("sponsor_slime.pssg", "sponsor_pack_b.pssg"), ("sponsor_yokohama.pssg", "sponsor_pack_c.pssg"), ("sponsor_quaife.pssg", "sponsor_pack_d.pssg") })
            Copy(Path.Combine(d3, "tracks/sponsors", pair.Item1), Path.Combine(track, pair.Item2));
        // This file only declares the default route and no fork sets.
        Copy(Path.Combine(donor, "route_1/dev_ai_track.xml"), Path.Combine(route, "dev_ai_track.xml"));
        // Use DiRT 2's character assets at Aspen's original crowd placements.
        Copy(Path.Combine(donor, "route_1/organism_track_dataset.xml"), Path.Combine(route, "organism_track_dataset.xml"));
        PortFiles.Json(Path.Combine(output,"crowds.json"),Crowds.Restore(track,source,d2,inputs,StaffResources.Roles,layout.SourceRoute));
        var snowPath=Path.Combine(sourceRoute,"snow.pssg"); inputs[snowPath]=PortFiles.Hash(snowPath);
        PortFiles.Json(Path.Combine(output,"static-snow.json"),StaticSnow.Bake(snowPath,Path.Combine(track,"tracksplit.pssg"),templates,shaders));
        PortFiles.Json(Path.Combine(output, "scene-props.json"), StaticProps.Bake(track));
        PortFiles.Json(Path.Combine(output, "terrain-containers.json"), TerrainContainers.Convert(track));
        PortFiles.Json(Path.Combine(output, "persistent-lights.json"), PersistentLights.Convert(track));
        if (layout == AspenLayout.SnowmassSprint)
            PortFiles.Json(Path.Combine(output, "hillside-beams.json"), TowerBeamFallback.Convert(track));
        PortFiles.Json(Path.Combine(output, "terrain-depth.json"), TerrainDepth.Convert(track));
        PortFiles.Json(Path.Combine(output, "terrain-visibility.json"), TerrainVisibility.Convert(track,source,TerrainVisibility.Mappings(System.Text.Json.JsonSerializer.SerializeToElement(placements)),layout.SourceRoute));
        PortFiles.Json(Path.Combine(output, "day-textures.json"), DayTextures.Resolve(track,condition));
        PortFiles.Json(Path.Combine(output, "terrain-occlusion.json"), TerrainOcclusion.Convert(track));
        if (layout == AspenLayout.ButtermilkClimb || layout == AspenLayout.ButtermilkDescent)
            PortFiles.Json(Path.Combine(output, "road-colour.json"), ButtermilkRoadColour.Convert(track,source,layout.SourceRoute));
        Progress?.Invoke("Rebuilding collision and checking geometry");
        var collision = Path.Combine(sourceRoute, "track.jpk"); inputs[collision] = PortFiles.Hash(collision);
        using (var stream = PortFiles.OpenRead(collision))
        {
            var jpk = new JpkFile(); jpk.Read(stream);
            var gltf = TrackGroundGltfConverter.Convert(TrackGround.Load(jpk));
            var before = Path.Combine(output, "ground.glb"); gltf.SaveGLB(before);
            var rebuilt = GltfTrackGroundConverter.Convert(gltf, VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
            using (var dest = new FileStream(Path.Combine(route, "track.jpk"), FileMode.CreateNew)) rebuilt.Save().Write(dest);
            using var verify = PortFiles.OpenRead(Path.Combine(route, "track.jpk"));
            var loaded = new JpkFile(); loaded.Read(verify);
            var after = Path.Combine(output, "ground-readback.glb"); TrackGroundGltfConverter.Convert(TrackGround.Load(loaded)).SaveGLB(after);
            var a = GeometryCheck.Read(before); var b = GeometryCheck.Read(after);
            int missing = GeometryCheck.Unmatched(a, b), added = GeometryCheck.Unmatched(b, a);
            PortFiles.Json(Path.Combine(output, "geometry.json"), new { InputTriangles = a.Count, OutputTriangles = b.Count, Missing = missing, Added = added, ToleranceMetres = .02, SurfaceAndWindingChecked = true });
            if (missing != 0 || added != 0) throw new InvalidDataException("Collision geometry changed.");
            var codes = a.Select(t => t.Material).Distinct().Order().ToArray();
            var surfacePath = Path.Combine(d2, "surface_materials.xml"); inputs[surfacePath] = PortFiles.Hash(surfacePath);
            PortFiles.WriteXml(SnowSurfaces.Create(PortFiles.ReadXml(surfacePath), codes), Path.Combine(output, "surface_materials.xml"));
            PortFiles.Json(Path.Combine(output, "surfaces.json"), codes.ToDictionary(c => c, c => SnowSurfaces.Recipes.TryGetValue(c[..3], out var recipe) ? (object)recipe : new { Native = c }));
        }
        PortFiles.Json(Path.Combine(output, "visibility.json"), VisibilityAudit.Check(File.ReadAllBytes(Path.Combine(route, "track.vis"))));
        Entities.Verify(routeEntities, PortFiles.ReadPssg(Path.Combine(route, "objects.ens")));
        PortFiles.Json(Path.Combine(output, "shaders.json"), shaders);
        PortFiles.Json(Path.Combine(output, "placements.json"), placements);
        PortFiles.Json(Path.Combine(output, "copies.json"), copies);
        Progress?.Invoke("Building track names, snow effects and lighting");
        var schema=SchemaPath ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../.deps/Ego-Engine-Modding/src/EgoDatabaseEditor/schema/schemaDirt2.xml"));
        inputs[schema]=PortFiles.Hash(schema);
        float length = AspenLayout.RouteLength(PortFiles.ReadXml(Path.Combine(route,"progress_track.xml")));
        AspenMetadata.Create(d2,schema,Path.Combine(output,"session-metadata"),inputs,layout,length,condition);
        SnowEffects.Build(track,d3,d2,Path.Combine(output,"session-effects"),inputs,layout);
        AmbientEffects.Build(track,d3,Path.Combine(output,"session-effects"),inputs,layout);
        ConditionLighting.Build(track,d3,d2,Path.Combine(output,"session-effects"),condition,inputs);
        // Import the staff animation schema only after all source scene files
        // have been converted; it must not alter the ski-lift animation file.
        StaffResources.Build(track,d2,fingerprints:inputs,layout:layout);
        PortFiles.Json(Path.Combine(output, "inputs.json"), inputs);
        foreach (var input in inputs) if (PortFiles.Hash(input.Key) != input.Value) throw new IOException("Input changed during conversion: " + input.Key);
        var hashes = Directory.EnumerateFiles(track, "*", SearchOption.AllDirectories).Order().ToDictionary(p => Path.GetRelativePath(track, p), PortFiles.Hash);
        PortFiles.Json(Path.Combine(output, "candidate.json"), new { Schema = 1, TrackId = layout.Id, SourceRoute = layout.SourceRoute, TrackDirectory = layout.DirectoryName, Condition = condition.Name, ConditionSource = condition.Suffix, Length = length, StaticChecksPassed = true, RuntimeValidated = false, Files = hashes, SurfaceHash = PortFiles.Hash(Path.Combine(output, "surface_materials.xml")), MetadataHash=PortFiles.Hash(Path.Combine(output,"session-metadata/metadata.json")), EffectsHash=PortFiles.Hash(Path.Combine(output,"session-effects/effects.json")) });
        Console.WriteLine("Candidate built; in-game validation is REQUIRED: " + output);
    }
}
