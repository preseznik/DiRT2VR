using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using System.Text.Json;

internal static class MizuBuild
{
    internal static void Run(string grid2,string dirt2,string output,Action<int,string> report)
    {
        string exe=Files.Inside(dirt2,"dirt2_game.exe");
        if(Files.Hash(exe)!="49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48")throw new InvalidDataException("Unsupported DiRT 2 executable");
        Files.NewOutput(output,grid2,dirt2);
        const bool fullCourse=true; const float length=0;
        report(10,"Reading Mizu Mountain scenery and road surfaces");
        var scene=MizuScene.Read(grid2,dirt2,output,length,fullCourse);string donor=Files.Inside(dirt2,"tracks/london/battersea");
        scene.Inputs[exe]=Files.Hash(exe);
        foreach(var file in Directory.EnumerateFiles(donor,"*",SearchOption.AllDirectories))scene.Inputs[file]=Files.Hash(file);
        Console.WriteLine($"Selected {scene.Visuals.Sum(m=>m.Indices.Length/3):N0} visual triangles in {scene.Visuals.Count:N0} batches");
        var builder=new QuadTreeMeshDataBuilder(VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
        foreach(var triangle in scene.Collision)builder.Add(triangle);
        var ground=TrackGround.Create(builder.Build()).Save();int collisionChunks=ground.Entries.Count-1;
        Files.Json(Path.Combine(output,"collision-budget.json"),new{CollisionChunks=collisionChunks,ObservedNativePoolCapacity=1290,ConservativeChunkBudget=1200,
            TerrainCorridorMetres=fullCourse?50:125,RigidPropCorridorMetres=fullCourse?25:110,DrivingCollisionModelsOnly=fullCourse,
            StaticSourcePrimitivesAdded=fullCourse,FullSourceCollisionRetained=false,RuntimeValidated=false});
        Console.WriteLine($"Collision chunks: {collisionChunks}");
        if(fullCourse&&collisionChunks>1200)throw new InvalidDataException("Collision archive exceeds the conservative native pool budget");
        using(var stream=new FileStream(Path.Combine(output,"track.jpk"),FileMode.CreateNew))ground.Write(stream);
        report(35,"Building Mizu scenery and visibility");
        var (_,after)=Visuals.Build(scene,donor,output,independentTiles:true);
        Visibility.WriteDiagnostic(Path.Combine(donor,"route_1/track.vis"),output,after);
        report(50,"Building the full route, checkpoints and safe resets");
        MizuRoute.Write(scene,donor,output);
        DonorScenery.Write(scene,donor,output);Lighting.Write(scene,donor,output);
        if(fullCourse)
        {
            string sourceLighting=Files.Inside(grid2,"tracks/locations/p2p/okutama/lighting_day.xml");
            scene.Inputs[sourceLighting]=Files.Hash(sourceLighting);
        }
        if(fullCourse)MizuSky.Write(scene,grid2,donor,output);
        report(60,"Preparing tunnel lighting and foliage");
        if(fullCourse)MizuFixtures.Write(grid2,dirt2,output);
        if(fullCourse)MizuFoliage.Write(grid2,output,scene.Origin,scene.Inputs);
        report(68,"Checking scenery and collision geometry");
        TrackBuild.Verify(scene,output);
        foreach(var (path,hash) in scene.Inputs)if(Files.Hash(path)!=hash)throw new InvalidDataException("Source changed while converting: "+path);
        Files.Json(Path.Combine(output,"inputs.json"),scene.Inputs);
        Files.Json(Path.Combine(output,"collision-materials.json"),scene.CollisionMappings);
        Files.Json(Path.Combine(output,"segment.json"),new{Schema=1,SourceLayout="GRID 2 Mizu Mountain route_0",LengthMetres=scene.Length,
            TranslationOnly=new[]{scene.Origin.X,scene.Origin.Y,scene.Origin.Z},DrivingGates=scene.Gates.Length,SurfaceTriangles=scene.Surfaces,
            RuntimeValidated=false,DistributionReady=false,FullCourse=fullCourse,SupportedMode="desktop-solo",NextGate="Native desktop appearance, driving and recovery"});
        if(fullCourse)
        {
            report(72,"Finishing car and particle lighting");
            MizuVehicleOcclusion.Write(grid2,output);
            string lighting=Path.Combine(output,"shared/lighting.xml");
            MizuParticleLighting.Write(grid2,lighting,lighting,Path.Combine(output,"particle-lighting.json"));
        }
        string[] extensions=[".pssg",".xml",".cqtc",".vis",".jpk",".ens",".bin",".clm"];
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Where(p=>extensions.Contains(Path.GetExtension(p)))
            .ToDictionary(p=>Path.GetRelativePath(output,p).Replace('\\','/'),p=>Files.Hash(p));
        Files.Json(Path.Combine(output,"validation.json"),new{Passed=true,RuntimeValidated=false,LengthMetres=scene.Length,Files=files});
        Console.WriteLine($"Mizu {(fullCourse?"full course":"segment")} built; gameplay acceptance remains pending: "+output);
    }
}
