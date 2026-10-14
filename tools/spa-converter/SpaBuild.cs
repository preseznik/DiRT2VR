using DiRT2VR.Nordschleife;
using System.Numerics;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;

internal static class SpaBuild
{
    internal static void Run(string source,string dirt2,string output)
    {
        string exe=Files.Inside(dirt2,"dirt2_game.exe");
        if(Files.Hash(exe)!="49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48")throw new InvalidDataException("Unsupported DiRT 2 executable");
        Files.NewOutput(output,source,dirt2);
        var scene=SpaScene.Read(source,dirt2,output,0,fullCourse:true);
        scene.Inputs[exe]=Files.Hash(exe);
        scene.Inputs[Files.Inside(source,"source.json")]=Files.Hash(Files.Inside(source,"source.json"));
        string donor=Files.Inside(dirt2,"tracks/london/battersea");
        foreach(var file in Directory.EnumerateFiles(donor).Concat(Directory.EnumerateFiles(Path.Combine(donor,"route_1"))))scene.Inputs[file]=Files.Hash(file);
        var original=new QuadTreeMeshDataBuilder(VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
        foreach(var triangle in scene.Collision)original.Add(triangle);
        var sourceCollision=scene.Collision.ToArray();
        var originalGround=TrackGround.Create(original.Build()).Save();
        // The accepted repair was applied after the first native encoding. Keep
        // its quantization and duplicate removal so clean builds retain that surface.
        scene.Collision.Clear();
        scene.Collision.AddRange(TrackGround.Load(originalGround).TraverseGrid().SelectMany(c=>c.QuadTree.GetTriangles()).DistinctBy(GeometryCoverage.Exact));
        bool Near(Vector3 p,float radius)=>scene.Gates.Any(g=>(g.Position.X-p.X)*(g.Position.X-p.X)+(g.Position.Z-p.Z)*(g.Position.Z-p.Z)<=radius*radius&&MathF.Abs(g.Position.Y-p.Y)<180);
        SpaGroundSupport.Add(scene,Files.Pssg(Files.Inside(source,"tracks/locations/europe/spa_francorchamps/tracksplit.pssg")),Near,output);
        SpaArmcoSupport.Add(scene,source,Near,output);
        var builder=new QuadTreeMeshDataBuilder(VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
        foreach(var triangle in scene.Collision)builder.Add(triangle);
        var ground=TrackGround.Create(builder.Build()).Save();int chunks=ground.Entries.Count-1;
        if(chunks>1200)throw new InvalidDataException("Collision archive exceeds conservative 1200 chunk budget");
        using(var stream=new FileStream(Path.Combine(output,"track.jpk"),FileMode.CreateNew))ground.Write(stream);
        Files.Json(Path.Combine(output,"collision-budget.json"),new{CollisionChunks=chunks,ConservativeChunkBudget=1200,TerrainCorridorMetres=50,PropCorridorMetres=25,ArmcoCorridorMetres=50,RuntimeValidated=false});
        Console.WriteLine($"Collision: {scene.Collision.Count:N0} triangles, {chunks} chunks; visuals {scene.Visuals.Sum(m=>m.Indices.Length/3):N0} triangles");
        // Grid height and recovery planes were created before the collision-only
        // repair. Derive them from the same unquantized source geometry.
        var repairedCollision=scene.Collision.ToArray();
        scene.Collision.Clear();scene.Collision.AddRange(sourceCollision);
        Visuals.Build(scene,donor,output,independentTiles:true);
        var after=SpaFoliage.ExpandBounds(scene,output);
        SpaTextureState.Preserve(output,source);
        SpaShaders.Write(dirt2,output);
        scene.Inputs[Files.Inside(dirt2,"dx11/shaderpack/shaderpack.pssg")]=Files.Hash(Files.Inside(dirt2,"dx11/shaderpack/shaderpack.pssg"));
        Visibility.WriteDiagnostic(Path.Combine(donor,"route_1/track.vis"),output,after);
        SpaRoute.Write(scene,source,donor,output);
        scene.Collision.Clear();scene.Collision.AddRange(repairedCollision);
        DonorScenery.Write(scene,donor,output);
        SpaMarkers.Write(scene,source,donor,output);
        SpaLighting.Write(scene,source,donor,output);
        string sky=Files.Inside(source,"tracks/locations/europe/spa_francorchamps/route_0/sky_day.pssg");
        scene.Inputs[sky]=Files.Hash(sky);
        MizuSky.PreserveSource(sky,Path.Combine(donor,"sky.pssg"),output,candidate:true);
        File.Copy(Path.Combine(output,"sky.pssg"),Path.Combine(output,"shared/sky.pssg"));
        TrackBuild.Verify(scene,output);
        foreach(var(path,hash)in scene.Inputs)if(Files.Hash(path)!=hash)throw new InvalidDataException("Source changed: "+path);
        Files.Json(Path.Combine(output,"inputs.json"),scene.Inputs);
        Files.Json(Path.Combine(output,"collision-materials.json"),scene.CollisionMappings);
        Files.Json(Path.Combine(output,"segment.json"),new{Schema=1,SourceLayout="GRID 2 Spa-Francorchamps GP Circuit route_0",LengthMetres=scene.Length,
            TranslationOnly=new[]{scene.Origin.X,scene.Origin.Y,scene.Origin.Z},FullCourse=true,Circuit=true,SupportedMode="desktop-solo",Daylight=true,RuntimeValidated=false,DistributionReady=false});
        string[] extensions=[".pssg",".xml",".cqtc",".vis",".jpk",".ens",".bin",".clm"];
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Where(p=>extensions.Contains(Path.GetExtension(p)) && !Path.GetFileName(p).StartsWith("added-",StringComparison.Ordinal))
            .ToDictionary(p=>Path.GetRelativePath(output,p).Replace('\\','/'),Files.Hash);
        Files.Json(Path.Combine(output,"validation.json"),new{Passed=true,RuntimeValidated=false,LengthMetres=scene.Length,Files=files});
        Console.WriteLine("Spa GP candidate built; native acceptance pending: "+output);
    }
}
