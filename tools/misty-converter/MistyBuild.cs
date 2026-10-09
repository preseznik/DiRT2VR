using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Xml;
namespace DiRT2VR.Misty;
internal static class MistyBuild
{
 internal static void Run(string ac,string d2,string output,Action<int,string>? progress)
 {
    bool fullCourse=true;
    string exe=Files.Inside(d2,"dirt2_game.exe");
    if(Files.Hash(exe)!="49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48")throw new InvalidDataException("Unsupported DiRT 2 executable.");
    Files.NewOutput(output,ac,d2);
    progress?.Invoke(10,"Reading Misty Loch geometry, materials and plant textures");
    var scene=MistyScene.Read(ac,d2,output,1000,fullCourse);string donor=Files.Inside(d2,"tracks/london/battersea");
    foreach(string file in Directory.EnumerateFiles(donor,"*",SearchOption.AllDirectories))scene.Inputs[file]=Files.Hash(file);
    scene.Inputs[exe]=Files.Hash(exe);
    string surfaces=Files.Inside(d2,"surface_materials.xml");scene.Inputs[surfaces]=Files.Hash(surfaces);
    using(var input=File.OpenRead(surfaces))
    {
        var xml=new XmlFile(input);var names=xml.Document.SelectNodes("//MATERIAL/@name")!.Cast<System.Xml.XmlNode>().Select(n=>n.Value!).ToHashSet();
        if(scene.Surfaces.Keys.Any(k=>!names.Contains(k)))throw new InvalidDataException("Unknown target collision surface.");
    }
    Console.WriteLine($"Selected {scene.Visuals.Sum(m=>m.Indices.Length/3):N0} visual / {scene.Collision.Count:N0} collision triangles; {scene.Length:F3} m.");
    progress?.Invoke(35,"Building the full circuit and roadside detail");
    var builder=new QuadTreeMeshDataBuilder(VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
    foreach(var triangle in scene.Collision)builder.Add(triangle);
    using(var stream=new FileStream(Path.Combine(output,"track.jpk"),FileMode.CreateNew))TrackGround.Create(builder.Build()).Save().Write(stream);
    var water=new MistyWater(scene,d2);
    var (before,after)=Visuals.Build(scene,donor,output,fullCourse);
    if(fullCourse)Visibility.WriteDiagnostic(Path.Combine(donor,"route_1/track.vis"),output,after);
    else Visibility.Write(Path.Combine(donor,"route_1/track.vis"),output,before,after);
    MistyRoute.Write(scene,donor,output);
    DonorScenery.Write(scene,donor,output);MistyLighting.Write(scene,donor,output);
    water.Write(output);
    MistyFlowShader.Write(scene,d2,output);
    MistyPresentation.Write(d2,output,scene.Gates[0].Position,scene.Gates[0].Tangent);
    progress?.Invoke(65,"Checking track geometry, progress and materials");
    TrackBuild.Verify(scene,output);
    MistyAudit.Run(scene,output);
    foreach(var (path,hash) in scene.Inputs)if(Files.Hash(path)!=hash)throw new InvalidDataException("Changed source: "+path);
    Files.Json(Path.Combine(output,"inputs.json"),scene.Inputs);
    Files.Json(Path.Combine(output,"collision-materials.json"),scene.CollisionMappings);
    Files.Json(Path.Combine(output,"segment.json"),new{Schema=1,SourceLayout="rt_misty_loch/normal",LengthMetres=scene.Length,TranslationOnly=new[]{scene.Origin.X,scene.Origin.Y,scene.Origin.Z},DrivingGates=scene.Gates.Length,SurfaceTriangles=scene.Surfaces,RuntimeValidated=false,DistributionReady=false,FullCourse=fullCourse,SupportedMode="desktop-solo",NextGate="Native desktop rendering, complete lap, timing/restart, barrier contact and off-road recovery"});
    if(fullCourse)Files.Json(Path.Combine(output,"full-audit.json"),new{Passed=true,TerrainTiles=after.Length,LengthMetres=scene.Length,SingleFinishIdentity=true,RuntimeValidated=false});
    var assets=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p) is ".pssg" or ".xml" or ".cqtc" or ".vis" or ".jpk" or ".ens" or ".bin" or ".lng")
        .ToDictionary(p=>Path.GetRelativePath(output,p).Replace('\\','/'),Files.Hash);
    Files.Json(Path.Combine(output,"validation.json"),new{Passed=true,RuntimeValidated=false,LengthMetres=scene.Length,Files=assets});
 }
}
