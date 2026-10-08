using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Nordschleife;

internal static class TrackBuild
{
    static string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
    static XElement Vector(string name,Vector3 v)=>new(name,new XAttribute("format","float3"),$"{F(v.X)} {F(v.Y)} {F(v.Z)}");
    internal static void RebuildFullRoute(string acRoot,string dirt2,string source,string output)
    {
        using var validation=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"validation.json")));
        using var segment=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"segment.json")));
        if(!validation.RootElement.GetProperty("Passed").GetBoolean()||!segment.RootElement.GetProperty("FullCourse").GetBoolean())throw new InvalidDataException("Expected an audited full-scene base.");
        foreach(var file in validation.RootElement.GetProperty("Files").EnumerateObject())if(Files.Hash(Files.Inside(source,file.Name))!=file.Value.GetString())throw new InvalidDataException("Base candidate changed: "+file.Name);
        using var inputs=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"inputs.json")));
        foreach(var input in inputs.RootElement.EnumerateObject())if(Files.Hash(input.Name)!=input.Value.GetString())throw new InvalidDataException("Base source changed: "+input.Name);
        Files.NewOutput(output,acRoot,dirt2,source);
        var scene=Scene.Read(acRoot,0,fullCourse:true);
        foreach(var input in scene.Inputs)if(!inputs.RootElement.TryGetProperty(input.Key,out var saved)||saved.GetString()!=input.Value)throw new InvalidDataException("Route source differs from base geometry.");
        Route(scene,Files.Inside(dirt2,"tracks/london/battersea"),output);
        foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
        {
            string relative=Path.GetRelativePath(source,file);if(relative is "validation.json" or "full-audit.json")continue;
            string target=Path.Combine(output,relative);if(File.Exists(target))continue;
            Files.NoLinks(file);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
        }
        var hashes=validation.RootElement.GetProperty("Files").EnumerateObject().ToDictionary(f=>f.Name,f=>Files.Hash(Files.Inside(output,f.Name)));
        Files.Json(Path.Combine(output,"validation.json"),new{Passed=true,RuntimeValidated=false,LengthMetres=scene.Length,Files=hashes});
        Files.Json(Path.Combine(output,"route-rebuild.json"),new{Base=source,GeometryAndTexturesRetained=true,RuntimeValidated=false});
        Console.WriteLine("Full route rebuilt; native validation remains pending: "+output);
    }
    internal static void Run(string acRoot,string dirt2,string output,float length,bool visibilityProbe=false,bool fullCourse=false)
    {
        string exe=Files.Inside(dirt2,"dirt2_game.exe");
        if(Files.Hash(exe)!="49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48")throw new InvalidDataException("Unsupported DiRT 2 executable.");
        Files.NewOutput(output,acRoot,dirt2);
        var scene=Scene.Read(acRoot,length,fullCourse);string donor=Files.Inside(dirt2,"tracks/london/battersea");
        foreach(var relative in new[]{"objects.pssg","trees.pssg","tracksplit.pssg","route_1/routesplit.pssg","route_1/track.vis","route_1/grids.pssg","route_1/ai_vehicle_track.xml"})
        {
            var path=Files.Inside(donor,relative);scene.Inputs[path]=Files.Hash(path);
        }
        scene.Inputs[exe]=Files.Hash(exe);
        string surfaces=Files.Inside(dirt2,"surface_materials.xml");scene.Inputs[surfaces]=Files.Hash(surfaces);
        using(var input=File.OpenRead(surfaces))
        {
            var xml=new XmlFile(input);var names=xml.Document.SelectNodes("//MATERIAL/@name")!.Cast<System.Xml.XmlNode>().Select(n=>n.Value!).ToHashSet();
            if(scene.Surfaces.Keys.Any(k=>!names.Contains(k)))throw new InvalidDataException("Collision uses an unknown stock surface.");
        }
        Console.WriteLine($"Selected {scene.Visuals.Sum(m=>m.Indices.Length/3):N0} visual and {scene.Collision.Count:N0} collision triangles.");
        var builder=new QuadTreeMeshDataBuilder(VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
        foreach(var triangle in scene.Collision)builder.Add(triangle);
        var ground=TrackGround.Create(builder.Build());
        using(var stream=new FileStream(Path.Combine(output,"track.jpk"),FileMode.CreateNew))ground.Save().Write(stream);
        var (before,after)=Visuals.Build(scene,donor,output,independentTiles:visibilityProbe||fullCourse);
        if(visibilityProbe||fullCourse)Visibility.WriteDiagnostic(Path.Combine(donor,"route_1/track.vis"),output,after);
        else Visibility.Write(Path.Combine(donor,"route_1/track.vis"),output,before,after);
        Route(scene,donor,output);
        DonorScenery.Write(scene,donor,output);
        Lighting.Write(scene,donor,output);
        Verify(scene,output);
        foreach(var (path,hash) in scene.Inputs)if(Files.Hash(path)!=hash)throw new InvalidDataException("Source changed while converting: "+path);
        Files.Json(Path.Combine(output,"inputs.json"),scene.Inputs);
        Files.Json(Path.Combine(output,"collision-materials.json"),scene.CollisionMappings);
        Files.Json(Path.Combine(output,"segment.json"),new{Schema=1,SourceLayout="ks_nordschleife/nordschleife",LengthMetres=scene.Length,
            TranslationOnly=new[]{scene.Origin.X,scene.Origin.Y,scene.Origin.Z},DrivingGates=scene.Gates.Length,SurfaceTriangles=scene.Surfaces,
            RuntimeValidated=false,DistributionReady=false,FullCourse=fullCourse,SupportedMode="desktop-solo",
            NextGate=fullCourse?"Full-course memory, visibility, timing, collision and reset validation; optimize LODs before VR":"Inspect appearance, camera-angle visibility, driving and resets on the gaming PC"});
        if(fullCourse)Files.Json(Path.Combine(output,"source-scene.json"),new{Meshes=scene.SourceMeshes,VisualTriangles=scene.Visuals.Sum(m=>m.Indices.Length/3),PhysicalTriangles=scene.Collision.Count,SceneCropping=false,DynamicMotionReproduced=false,RuntimeValidated=false});
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p) is ".pssg" or ".xml" or ".cqtc" or ".vis" or ".jpk" or ".ens" or ".bin")
            .ToDictionary(p=>Path.GetRelativePath(output,p).Replace('\\','/'),p=>Files.Hash(p));
        Files.Json(Path.Combine(output,"validation.json"),new{Passed=true,RuntimeValidated=false,LengthMetres=scene.Length,Files=files});
        Console.WriteLine((fullCourse?"Full-course diagnostic":"Segment")+" built; gameplay acceptance remains pending: "+output);
    }
    static void Route(Scene scene,string donor,string output)
    {
        float leadIn=scene.FullCourse?StartingGrid.ApproachMetres:20;
        const float pointPadding=5;
        var start=scene.Gates[0];var tangent=Vector3.Normalize(new Vector3(start.Tangent.X,0,start.Tangent.Z));
        var approachGate=scene.FullCourse?scene.BeforeStart(leadIn):start with{Position=start.Position-tangent*leadIn};
        var approach=approachGate.Position;approach.Y=scene.RoadHeight(approach)+(start.Position.Y-scene.RoadHeight(start.Position));
        // Stock point-to-point routes have an approach gate before the start split.
        // Include the grid in the centre-line range rather than beginning ahead of the car.
        // A one-lap tour uses distinct start/end gate identities at the same physical
        // finish line. Point-to-point HUD data displays timing checkpoint markers.
        var routeGates=(scene.FullCourse?scene.Gates.Append(start with{Distance=scene.Length}):scene.Gates.Concat(scene.RunoutGates))
            .Select(g=>g with{Distance=g.Distance+leadIn+pointPadding}).Prepend(approachGate with{Position=approach,Distance=pointPadding}).ToArray();
        // The native progress HUD supports ten splits including start and finish.
        // Divide the full course into nine sectors, about 2.3 km each.
        var checkpointDistances=scene.FullCourse?Enumerable.Range(1,8).Select(i=>i*scene.Length/9).ToArray():[];
        var checkpointIndices=checkpointDistances.Select(d=>Enumerable.Range(0,scene.Gates.Length).MinBy(i=>MathF.Abs(scene.Gates[i].Distance-d))).ToArray();
        var crossingGates=scene.FullCourse?ProgressGates.Indices(scene.Gates,ProgressGates.NativeLimit-2,checkpointIndices)
            .Select(i=>routeGates[i+1]).Prepend(routeGates[0]).Append(routeGates[^1]).ToArray():routeGates;
        var aiGates=scene.FullCourse?ProgressGates.Indices(scene.Gates,ProgressGates.AiGateBudget).Select(i=>scene.Gates[i]).ToArray():routeGates;
        var gates=new XElement("gates",new XAttribute("num_gates",aiGates.Length));
        var progress=new XElement("gates",new XAttribute("num_gates",crossingGates.Length));
        var links=new XElement("links",new XAttribute("num_links",aiGates.Length-(scene.FullCourse?0:1)));
        for(int i=0;i<aiGates.Length;i++)
        {
            var gate=aiGates[i];var left=Vector3.Normalize(new Vector3(gate.Tangent.Z,0,-gate.Tangent.X));float width=gate.Left+gate.Right;
            XElement Waypoint(int id,string type,float at)=>new("waypoint",new XAttribute("id",id),new XAttribute("type",type),new XAttribute("length",F(at)),type=="racing_line"?new XElement("racing_line",new XAttribute("type","optimal")):null);
            gates.Add(new XElement("gate",new XAttribute("id",i),Vector("position",gate.Position+left*gate.Left),Vector("normal",-left),
                new XElement("waypoints",new XAttribute("num_waypoints",5),Waypoint(0,"left_track_limit",0),Waypoint(1,"racing_line",gate.Left),Waypoint(2,"right_track_limit",width),Waypoint(3,"left_racing_limit",MathF.Min(1,gate.Left/2)),Waypoint(4,"right_racing_limit",width-MathF.Min(1,gate.Right/2))),new XElement("edges",new XAttribute("num_edges",0))));
            if(i+1<aiGates.Length || scene.FullCourse)links.Add(new XElement("link",new XAttribute("id",i),new XAttribute("from_gate",i),new XAttribute("to_gate",(i+1)%aiGates.Length)));
        }
        for(int i=0;i<crossingGates.Length;i++)
        {
            var gate=crossingGates[i];var left=Vector3.Normalize(new Vector3(gate.Tangent.Z,0,-gate.Tangent.X));
            progress.Add(new XElement("gate",new XAttribute("id",i),new XAttribute("distance",F(gate.Distance)),Vector("left",gate.Position+left*(gate.Left+1)),Vector("right",gate.Position-left*(gate.Right+1))));
        }
        var ai=new XDocument(new XElement("ai_track_data",new XAttribute("version_major",3),new XAttribute("version_minor",0),new XAttribute("version_revision",2),new XElement("track",new XAttribute("name","default"),gates,links,new XElement("brake_lines",new XAttribute("num_brake_lines",0)),new XElement("min_speed_lines",new XAttribute("num_min_speed_lines",0)),new XElement("retire_lines",new XAttribute("num_retire_lines",0)),new XElement("fork_sets",new XAttribute("num_fork_sets",0)))));
        Files.Xml(ai,Path.Combine(output,"ai_track.xml"));Files.Xml(ai,Path.Combine(output,"dev_ai_track.xml"));
        var splits=new XElement("route",new XAttribute("id",0),new XAttribute("direction","forwards"),new XAttribute("num_splits",scene.FullCourse?checkpointDistances.Length+2:5));
        // The demo session uses circuit lap accounting even for an open progress route.
        // Its first finish crossing initializes the pending lap (-1 to 0), as on stock
        // circuits; the final crossing can then complete lap 1 and dispatch the finish.
        var splitGates=scene.FullCourse?checkpointDistances.Select(d=>(Enumerable.Range(1,crossingGates.Length-2).MinBy(i=>MathF.Abs(crossingGates[i].Distance-(d+leadIn+pointPadding))),"time"))
            .Prepend((1,"finish")).Append((crossingGates.Length-1,"finish")).ToArray():
            new[]{(1,"finish"),(scene.Gates.Length/4+1,"time"),(scene.Gates.Length/2+1,"time"),(scene.Gates.Length*3/4+1,"time"),(scene.Gates.Length,"finish")};
        foreach(var (gate,type) in splitGates)
            splits.Add(new XElement("split",new XAttribute("id",splits.Elements().Count()),new XAttribute("type",type),new XAttribute("gate",gate)));
        // The HUD route and distance calculation use centre-line points, not just crossing gates.
        // The native open-route mapper needs strict plane crossings inside line segments;
        // a vertex on a gate plane can leave its segment reference at -1.
        var before=scene.FullCourse?scene.BeforeStart(leadIn+pointPadding).Position:routeGates[0].Position-tangent*pointPadding;before.Y=scene.RoadHeight(before)+(start.Position.Y-scene.RoadHeight(start.Position));
        var after=routeGates[^1].Position+routeGates[^1].Tangent*pointPadding;
        var lineGates=routeGates.Zip(routeGates.Skip(1),(a,b)=>new Gate((a.Position+b.Position)/2,Vector3.Normalize(a.Tangent+b.Tangent),(a.Left+b.Left)/2,(a.Right+b.Right)/2,(a.Distance+b.Distance)/2))
            .Prepend(routeGates[0] with{Position=before,Distance=.01f}).Append(routeGates[^1] with{Position=after,Distance=routeGates[^1].Distance+pointPadding}).ToArray();
        int denseLinePoints=lineGates.Length;
        if(scene.FullCourse)
        {
            var originalIndices=routeGates.Select((g,i)=>(g.Distance,i)).ToDictionary(p=>p.Distance,p=>p.i);
            // Keep both original midpoint samples around every progress plane,
            // then spend the remaining point budget on bends and crests.
            var protectedPoints=crossingGates.SelectMany(g=>new[]{originalIndices[g.Distance],originalIndices[g.Distance]+1});
            lineGates=ProgressGates.Indices(lineGates,ProgressGates.PointLineBudget,protectedPoints).Select(i=>lineGates[i]).ToArray();
        }
        var points=new XElement("points",new XAttribute("num_lines",3));
        foreach(var (name,type,side) in new[]{("progress_centre_line_0","progress",0),("terminal_camera_line_right_r0_00","camera",-1),("terminal_camera_line_left_r0_00","camera",1)})
        {
            var samples=lineGates;
            var line=new XElement("line",new XAttribute("name",name),new XAttribute("type",type),new XAttribute("num_points",samples.Length));
            for(int i=0;i<samples.Length;i++)
            {
                var gate=samples[i];var left=Vector3.Normalize(new Vector3(gate.Tangent.Z,0,-gate.Tangent.X));
                var position=gate.Position+left*(side>0?gate.Left:side<0?-gate.Right:0);
                line.Add(new XElement("point",new XAttribute("id",i),new XAttribute("distance",F(gate.Distance)),Vector("position",position)));
            }
            points.Add(line);
        }
        Files.Xml(new XDocument(new XElement("progress_track_data",new XAttribute("exporter_version","3.0.0"),new XElement("track",new XAttribute("type","point_to_point"),new XAttribute("total_distance",F(lineGates[^1].Distance+.01f))),
            new XElement("routes",new XAttribute("num_routes",1),splits),progress,points)),Path.Combine(output,"progress_track.xml"));
        Files.Json(Path.Combine(output,"progress.json"),new{DenseSourceLinePoints=denseLinePoints,CentreLinePoints=lineGates.Length,PointLineBudget=ProgressGates.PointLineBudget,AiGates=aiGates.Length,AiGateBudget=ProgressGates.AiGateBudget,ResetGates=scene.Gates.Length,ProgressGates=crossingGates.Length,NativeProgressGateLimit=ProgressGates.NativeLimit,ApproachMetres=leadIn,PointPaddingMetres=pointPadding,RunoutMetres=scene.FullCourse?pointPadding:routeGates[^1].Distance-scene.Length-leadIn-pointPadding,StartGate=1,StartSplit="finish",DemoLapInitialization=true,FinishGate=scene.FullCourse?crossingGates.Length-1:scene.Gates.Length,TimingGates=splitGates.Where(s=>s.Item2=="time").Select(s=>s.Item1),CheckpointSpacingMetres=scene.FullCourse?scene.Length/9:0,OneLapTour=scene.FullCourse,FullCourse=scene.FullCourse,RuntimeValidated=false});
        StartingGrid.Write(scene,donor,output);
        using(var input=File.OpenRead(Path.Combine(donor,"route_1/ai_vehicle_track.xml")))
        {
            var vehicle=new XmlFile(input);foreach(System.Xml.XmlElement brake in vehicle.Document.SelectNodes("//brake_lines")!){brake.RemoveAll();brake.SetAttribute("num_brake_lines","0");}
            using var stream=new FileStream(Path.Combine(output,"ai_vehicle_track.xml"),FileMode.CreateNew);vehicle.Write(stream);
        }
        Files.Xml(new XDocument(new XElement("route",new XElement("default",new XAttribute("track_cull_dist",scene.FullCourse?"20000.0":"1600.0"),new XAttribute("world_cull_dist",scene.FullCourse?"20000.0":"2000.0"),new XAttribute("track_lod_dist","1200.0"),new XAttribute("fade","10.0")),new XElement("Systems",new XAttribute("tree_settings","low"),new XAttribute("ornament_settings","medium")))),Path.Combine(output,"route_overrides.xml"));
        var recovery=new RouteRecovery(scene.Collision);
        foreach(var (name,material) in new[]{("boundarylines.cqtc","FBND"),("resetlines.cqtc","RESE"),("cameralines.cqtc","RESD")})
        {
            var planes=new QuadTreeMeshDataBuilder(CQuadTreeTypeInfo.Get(CQuadTreeType.Dirt));
            for(int i=0;i<scene.Gates.Length-(scene.FullCourse?0:1);i++)
            {
                var g=scene.Gates[i];var next=scene.Gates[(i+1)%scene.Gates.Length];var left=new Vector3(g.Tangent.Z,0,-g.Tangent.X);var nextLeft=new Vector3(next.Tangent.Z,0,-next.Tangent.X);
                if(material=="FBND")foreach(int side in new[]{-1,1})
                {
                    var a=g.Position+left*(side*(side>0?g.Left+20:g.Right+20));var b=next.Position+nextLeft*(side*(side>0?next.Left+20:next.Right+20));
                    var c=b+Vector3.UnitY*4;var d=a+Vector3.UnitY*4;planes.Add(new(a,b,c,material));planes.Add(new(a,c,d,material));planes.Add(new(c,b,a,material));planes.Add(new(d,c,a,material));
                }
                else
                {
                    var a=g.Position+left*(g.Left+35);var b=next.Position+nextLeft*(next.Left+35);
                    var c=next.Position-nextLeft*(next.Right+35);var d=g.Position-left*(g.Right+35);
                    float floor=recovery.Floor(a,b,c,d);a.Y=b.Y=c.Y=d.Y=floor;
                    planes.Add(new(a,b,c,material));planes.Add(new(a,c,d,material));
                }
            }
            File.WriteAllBytes(Path.Combine(output,name),CQuadTreeFile.Create(planes.Build()).Bytes);
        }
        Files.Json(Path.Combine(output,"gates.json"),scene.Gates.Select(g=>new{Position=new[]{g.Position.X,g.Position.Y,g.Position.Z},Tangent=new[]{g.Tangent.X,g.Tangent.Y,g.Tangent.Z},g.Distance,g.Left,g.Right}));
    }
    internal static void Verify(Scene scene,string output)
    {
        using var stream=File.OpenRead(Path.Combine(output,"track.jpk"));var archive=new JpkFile();archive.Read(stream);var ground=TrackGround.Load(archive);
        var readback=ground.TraverseGrid().SelectMany(c=>c.QuadTree.GetTriangles()).ToArray();
        // Cell-boundary partitioning can repeat collision triangles. Match cyclic winding and surfaces
        // using actual distances; rounded coordinate buckets alone can falsely reject near-boundary vertices.
        int missing=GeometryCoverage.Unmatched(scene.Collision,readback),extra=GeometryCoverage.Unmatched(readback,scene.Collision);
        if(missing!=0||extra!=0)throw new InvalidDataException($"Collision coverage mismatch: missing {missing}, extra {extra}.");
        var materials=scene.Visuals.Select(m=>m.Material).DistinctBy(m=>m.Id).OrderBy(m=>m.Id,StringComparer.Ordinal).Select((m,i)=>(m.Id,Name:"nord_material_"+i)).ToDictionary(x=>x.Id,x=>x.Name);
        var expectedVisual=new Dictionary<GeometryCoverage.ExactFace,int>();
        foreach(var mesh in scene.Visuals)for(int i=0;i<mesh.Indices.Length;i+=3)
        {
            var key=GeometryCoverage.Exact(new(mesh.Positions[mesh.Indices[i]],mesh.Positions[mesh.Indices[i+1]],mesh.Positions[mesh.Indices[i+2]],materials[mesh.Material.Id]));
            expectedVisual[key]=expectedVisual.GetValueOrDefault(key)+1;
        }
        var actualVisual=new Dictionary<GeometryCoverage.ExactFace,int>();
        var visuals=Files.Pssg(Path.Combine(output,"routesplit.pssg"));int triangles=0;
        foreach(var mesh in visuals.Elements<PssgRenderDataSource>())
        {
            var draw=visuals.Elements<PssgRenderStreamInstance>().First(x=>x.Indices=="#"+mesh.Id);
            if(draw.GetShaderInstance().ShaderGroup=="#batched_track.fx")continue;
            var reader=new EgoEngineLibrary.Formats.Pssg.RenderDataSourceReader(mesh);if(reader.IndexCount%3!=0 || mesh.Primitive!="triangles")throw new InvalidDataException("Invalid visual primitive.");
            for(int i=0;i<reader.IndexCount;i++)if(!Kn5.Finite(reader.GetPosition(reader.GetIndex(i))))throw new InvalidDataException("Invalid visual position.");
            string material=visuals.Elements<PssgRenderStreamInstance>().First(x=>x.Indices=="#"+mesh.Id).Shader[1..];
            for(int i=0;i<reader.IndexCount;i+=3)
            {
                var key=GeometryCoverage.Exact(new(reader.GetPosition(reader.GetIndex(i)),reader.GetPosition(reader.GetIndex(i+1)),reader.GetPosition(reader.GetIndex(i+2)),material));
                actualVisual[key]=actualVisual.GetValueOrDefault(key)+1;
            }
            triangles+=checked((int)(reader.IndexCount/3));
        }
        if(triangles!=scene.Visuals.Sum(m=>m.Indices.Length/3))throw new InvalidDataException("Visual triangle count changed.");
        if(expectedVisual.Count!=actualVisual.Count||expectedVisual.Any(p=>actualVisual.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidDataException("Visual positions, winding, materials or triangle multiplicity changed.");
        foreach(var instance in visuals.Elements<PssgRenderStreamInstance>()){_=instance.GetRenderDataSource();_=instance.GetShaderInstance();}
        Files.Json(Path.Combine(output,"geometry-check.json"),new{Passed=true,SourceCollisionTriangles=scene.Collision.Count,DecodedCollisionTriangles=readback.Length,MissingCollisionTriangles=missing,ExtraCollisionTriangles=extra,GeometryToleranceMetres=.02,SurfaceAndWindingChecked=true,VisualTriangles=triangles,VisualPositionsWindingMaterialsAndMultiplicityExact=true,RuntimeValidated=false});
        TrackGroundGltfConverter.Convert(ground).SaveGLB(Path.Combine(output,"collision-readback.glb"));
    }
}
