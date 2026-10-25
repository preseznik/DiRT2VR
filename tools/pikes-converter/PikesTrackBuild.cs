using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Nordschleife;

internal static class PikesTrackBuild
{
    static string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
    static XElement Vector(string name,Vector3 v)=>new(name,new XAttribute("format","float3"),$"{F(v.X)} {F(v.Y)} {F(v.Z)}");
    internal static void Route(Scene scene,string donor,string output)
    {
        const float leadIn=20;
        const float pointPadding=5;
        var start=scene.Gates[0];var tangent=Vector3.Normalize(new Vector3(start.Tangent.X,0,start.Tangent.Z));
        var approach=start.Position-tangent*leadIn;approach.Y=scene.RoadHeight(approach)+(start.Position.Y-scene.RoadHeight(start.Position));
        // Stock point-to-point routes have an approach gate before the start split.
        // Include the grid in the centre-line range rather than beginning ahead of the car.
        // Full Pikes is also point-to-point; retain its summit runout instead
        // of linking the final gate back to the starting line.
        var routeGates=scene.Gates.Concat(scene.RunoutGates)
            .Select(g=>g with{Distance=g.Distance+leadIn+pointPadding}).Prepend(start with{Position=approach,Distance=pointPadding}).ToArray();
        // The native progress HUD supports ten splits including start and finish.
        bool sector=scene.Length>1000;
        using var sourceRoute=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"source-route.json")));
        var sourceCheckpoints=sector?sourceRoute.RootElement.GetProperty("CheckpointDistances").EnumerateArray().Select(d=>d.GetSingle()).ToArray():[];
        if(sourceCheckpoints.Length>8&&(!scene.FullCourse||sourceCheckpoints.Length!=9))throw new InvalidDataException("Source checkpoints exceed the native HUD budget");
        var omittedCheckpoints=sourceCheckpoints.Length==9?new[]{sourceCheckpoints[4]}:[];
        var checkpointDistances=sourceCheckpoints.Except(omittedCheckpoints).ToArray();
        var checkpointIndices=checkpointDistances.Select(d=>Enumerable.Range(0,scene.Gates.Length).MinBy(i=>MathF.Abs(scene.Gates[i].Distance-d))).ToArray();
        var crossingGates=sector?
            ProgressGates.Indices(routeGates,ProgressGates.NativeLimit,checkpointIndices.Select(i=>i+1).Append(1).Append(scene.Gates.Length)).Select(i=>routeGates[i]).ToArray():routeGates;
        var aiGates=scene.FullCourse?ProgressGates.Indices(routeGates,ProgressGates.AiGateBudget).Select(i=>routeGates[i]).ToArray():routeGates;
        var gates=new XElement("gates",new XAttribute("num_gates",aiGates.Length));
        var progress=new XElement("gates",new XAttribute("num_gates",crossingGates.Length));
        var links=new XElement("links",new XAttribute("num_links",aiGates.Length-1));
        for(int i=0;i<aiGates.Length;i++)
        {
            var gate=aiGates[i];var left=Vector3.Normalize(new Vector3(gate.Tangent.Z,0,-gate.Tangent.X));float width=gate.Left+gate.Right;
            XElement Waypoint(int id,string type,float at)=>new("waypoint",new XAttribute("id",id),new XAttribute("type",type),new XAttribute("length",F(at)),type=="racing_line"?new XElement("racing_line",new XAttribute("type","optimal")):null);
            gates.Add(new XElement("gate",new XAttribute("id",i),Vector("position",gate.Position+left*gate.Left),Vector("normal",-left),
                new XElement("waypoints",new XAttribute("num_waypoints",5),Waypoint(0,"left_track_limit",0),Waypoint(1,"racing_line",gate.Left),Waypoint(2,"right_track_limit",width),Waypoint(3,"left_racing_limit",MathF.Min(1,gate.Left/2)),Waypoint(4,"right_racing_limit",width-MathF.Min(1,gate.Right/2))),new XElement("edges",new XAttribute("num_edges",0))));
            if(i+1<aiGates.Length)links.Add(new XElement("link",new XAttribute("id",i),new XAttribute("from_gate",i),new XAttribute("to_gate",i+1)));
        }
        for(int i=0;i<crossingGates.Length;i++)
        {
            var gate=crossingGates[i];var left=Vector3.Normalize(new Vector3(gate.Tangent.Z,0,-gate.Tangent.X));
            progress.Add(new XElement("gate",new XAttribute("id",i),new XAttribute("distance",F(gate.Distance)),Vector("left",gate.Position+left*(gate.Left+1)),Vector("right",gate.Position-left*(gate.Right+1))));
        }
        var ai=new XDocument(new XElement("ai_track_data",new XAttribute("version_major",3),new XAttribute("version_minor",0),new XAttribute("version_revision",2),new XElement("track",new XAttribute("name","default"),gates,links,new XElement("brake_lines",new XAttribute("num_brake_lines",0)),new XElement("min_speed_lines",new XAttribute("num_min_speed_lines",0)),new XElement("retire_lines",new XAttribute("num_retire_lines",0)),new XElement("fork_sets",new XAttribute("num_fork_sets",0)))));
        Files.Xml(ai,Path.Combine(output,"ai_track.xml"));Files.Xml(ai,Path.Combine(output,"dev_ai_track.xml"));
        var splits=new XElement("route",new XAttribute("id",0),new XAttribute("direction","forwards"),new XAttribute("num_splits",scene.FullCourse||sector?checkpointDistances.Length+2:5));
        // The demo session uses circuit lap accounting even for an open progress route.
        // Its first finish crossing initializes the pending lap (-1 to 0), as on stock
        // circuits; the final crossing can then complete lap 1 and dispatch the finish.
        int finishIndex=Enumerable.Range(1,crossingGates.Length-1).MinBy(i=>MathF.Abs(crossingGates[i].Distance-(scene.Length+leadIn+pointPadding)));
        var splitGates=scene.FullCourse||sector?checkpointDistances.Select(d=>(Enumerable.Range(1,crossingGates.Length-2).MinBy(i=>MathF.Abs(crossingGates[i].Distance-(d+leadIn+pointPadding))),"time"))
            .Prepend((1,"finish")).Append((finishIndex,"finish")).ToArray():
            new[]{(1,"finish"),(scene.Gates.Length/4+1,"time"),(scene.Gates.Length/2+1,"time"),(scene.Gates.Length*3/4+1,"time"),(scene.Gates.Length,"finish")};
        foreach(var (gate,type) in splitGates)
            splits.Add(new XElement("split",new XAttribute("id",splits.Elements().Count()),new XAttribute("type",type),new XAttribute("gate",gate)));
        // The HUD route and distance calculation use centre-line points, not just crossing gates.
        // The native open-route mapper needs strict plane crossings inside line segments;
        // a vertex on a gate plane can leave its segment reference at -1.
        var before=routeGates[0].Position-tangent*pointPadding;before.Y=scene.RoadHeight(before)+(start.Position.Y-scene.RoadHeight(start.Position));
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
        Files.Json(Path.Combine(output,"progress.json"),new{DenseSourceLinePoints=denseLinePoints,CentreLinePoints=lineGates.Length,PointLineBudget=ProgressGates.PointLineBudget,AiGates=aiGates.Length,AiGateBudget=ProgressGates.AiGateBudget,ResetGates=scene.Gates.Length,ProgressGates=crossingGates.Length,NativeProgressGateLimit=ProgressGates.NativeLimit,ApproachMetres=leadIn,PointPaddingMetres=pointPadding,RunoutMetres=routeGates[^1].Distance-scene.Length-leadIn-pointPadding,StartGate=1,StartSplit="finish",DemoLapInitialization=true,FinishGate=splitGates[^1].Item1,TimingGates=splitGates.Where(s=>s.Item2=="time").Select(s=>s.Item1),OmittedSourceCheckpoints=omittedCheckpoints,OneLapTour=false,FullCourse=scene.FullCourse,RuntimeValidated=false});
        var grids=Files.Pssg(Path.Combine(donor,"route_1/grids.pssg"));var right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,-tangent));
        var spawn=start.Position-tangent*10;float roadHeight=scene.RoadHeight(spawn);spawn.Y=roadHeight+.6f;
        foreach(var node in grids.Elements<PssgNode>())
        {
            node.Transform.Transform=Matrix4x4.Identity;
            if(node.Id.StartsWith("slot_",StringComparison.Ordinal))node.Transform.Transform=new(right.X,right.Y,right.Z,0,0,1,0,0,-tangent.X,-tangent.Y,-tangent.Z,0,spawn.X,spawn.Y,spawn.Z,1);
        }
        Files.Save(grids,Path.Combine(output,"grids.pssg"));
        Files.Json(Path.Combine(output,"grid.json"),new{Position=new[]{spawn.X,spawn.Y,spawn.Z},RoadHeight=roadHeight,ClearanceMetres=.6f,BehindStartMetres=10,RuntimeValidated=false});
        using(var input=File.OpenRead(Path.Combine(donor,"route_1/ai_vehicle_track.xml")))
        {
            var vehicle=new XmlFile(input);foreach(System.Xml.XmlElement brake in vehicle.Document.SelectNodes("//brake_lines")!){brake.RemoveAll();brake.SetAttribute("num_brake_lines","0");}
            using var stream=new FileStream(Path.Combine(output,"ai_vehicle_track.xml"),FileMode.CreateNew);vehicle.Write(stream);
        }
        Files.Xml(new XDocument(new XElement("route",new XElement("default",new XAttribute("track_cull_dist","1600.0"),new XAttribute("world_cull_dist","2000.0"),new XAttribute("track_lod_dist","1200.0"),new XAttribute("fade","10.0")),new XElement("Systems",new XAttribute("tree_settings","low"),new XAttribute("ornament_settings","medium")))),Path.Combine(output,"route_overrides.xml"));
        var recovery=new RouteRecovery(scene.Collision.Where(t=>scene.RoadSurfaces.Contains(t.Material)).Select(t=>t with{Material="RDT+"}));
        int omittedBoundaries=0;
        foreach(var (name,material) in new[]{("boundarylines.cqtc","FBND"),("resetlines.cqtc","RESE"),("cameralines.cqtc","RESD")})
        {
            var planes=new QuadTreeMeshDataBuilder(CQuadTreeTypeInfo.Get(CQuadTreeType.Dirt));
            for(int i=0;i<scene.Gates.Length-1;i++)
            {
                var g=scene.Gates[i];var next=scene.Gates[(i+1)%scene.Gates.Length];var left=new Vector3(g.Tangent.Z,0,-g.Tangent.X);var nextLeft=new Vector3(next.Tangent.Z,0,-next.Tangent.X);
                if(material=="FBND")foreach(int side in new[]{-1,1})
                {
                    var a=g.Position+left*(side*(side>0?g.Left+20:g.Right+20));var b=next.Position+nextLeft*(side*(side>0?next.Left+20:next.Right+20));
                    // An offset from one leg of a hairpin can run through another
                    // leg. Do not turn that offset into an invisible road barrier.
                    if(scene.Gates.Zip(scene.Gates.Skip(1)).Any(pair=>BoundaryNearRoad(a,b,pair.First,pair.Second))){omittedBoundaries++;continue;}
                    var c=b+Vector3.UnitY*4;var d=a+Vector3.UnitY*4;planes.Add(new(a,b,c,material));planes.Add(new(a,c,d,material));planes.Add(new(c,b,a,material));planes.Add(new(d,c,a,material));
                }
                else
                {
                    var a=g.Position+left*(g.Left+35);var b=next.Position+nextLeft*(next.Left+35);
                    var c=next.Position-nextLeft*(next.Right+35);var d=g.Position-left*(g.Right+35);
                    float floor=recovery.Floor(a,b,c,d);a.Y=b.Y=c.Y=d.Y=floor;
                    planes.Add(new(a,b,c,material));planes.Add(new(a,c,d,material));
                    planes.Add(new(c,b,a,material));planes.Add(new(d,c,a,material));
                }
            }
            if(material=="RESE")
            {
                // A final catch plane covers gaps between offset strips and their
                // outer edge. Keep it below all collision, so it cannot reset a
                // car driving on a lower nearby leg of the route.
                var vertices=scene.Collision.SelectMany(t=>new[]{t.Position0,t.Position1,t.Position2}).ToArray();
                float floor=vertices.Min(p=>p.Y)-10;
                float minX=vertices.Min(p=>p.X)-100,maxX=vertices.Max(p=>p.X)+100,minZ=vertices.Min(p=>p.Z)-100,maxZ=vertices.Max(p=>p.Z)+100;
                var a=new Vector3(minX,floor,minZ);var b=new Vector3(maxX,floor,minZ);var c=new Vector3(maxX,floor,maxZ);var d=new Vector3(minX,floor,maxZ);
                planes.Add(new(a,b,c,material));planes.Add(new(a,c,d,material));planes.Add(new(c,b,a,material));planes.Add(new(d,c,a,material));
            }
            File.WriteAllBytes(Path.Combine(output,name),CQuadTreeFile.Create(planes.Build()).Bytes);
        }
        Files.Json(Path.Combine(output,"boundary-recovery.json"),new{HairpinBoundarySectionsOmitted=omittedBoundaries,DoubleSidedRecovery=true,GlobalRecoveryFloor=true,RuntimeValidated=false});
        Files.Json(Path.Combine(output,"gates.json"),scene.Gates.Select(g=>new{Position=new[]{g.Position.X,g.Position.Y,g.Position.Z},Tangent=new[]{g.Tangent.X,g.Tangent.Y,g.Tangent.Z},g.Distance,g.Left,g.Right}));
    }
    static bool BoundaryNearRoad(Vector3 a,Vector3 b,Gate first,Gate next)
    {
        if(MathF.Max(first.Position.Y,next.Position.Y)+2<MathF.Min(a.Y,b.Y)||MathF.Min(first.Position.Y,next.Position.Y)-2>MathF.Max(a.Y,b.Y)+4)return false;
        var p=new Vector2(a.X,a.Z);var q=new Vector2(b.X,b.Z);var r=new Vector2(first.Position.X,first.Position.Z);var s=new Vector2(next.Position.X,next.Position.Z);
        float Cross(Vector2 u,Vector2 v)=>u.X*v.Y-u.Y*v.X;
        float PointDistance(Vector2 point,Vector2 start,Vector2 end){var d=end-start;float t=d.LengthSquared()<1e-8f?0:Math.Clamp(Vector2.Dot(point-start,d)/d.LengthSquared(),0,1);return Vector2.Distance(point,start+d*t);}
        float denominator=Cross(q-p,s-r);
        if(MathF.Abs(denominator)>1e-6f){float t=Cross(r-p,s-r)/denominator,u=Cross(r-p,q-p)/denominator;if(t>=0&&t<=1&&u>=0&&u<=1)return true;}
        float distance=MathF.Min(MathF.Min(PointDistance(p,r,s),PointDistance(q,r,s)),MathF.Min(PointDistance(r,p,q),PointDistance(s,p,q)));
        return distance<=MathF.Max(MathF.Max(first.Left,first.Right),MathF.Max(next.Left,next.Right))+2;
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
            var draw=visuals.Elements<PssgRenderStreamInstance>().First(x=>x.Indices=="#"+mesh.Id && x.GetShaderInstance().ShaderGroup is not ("#pikes_foliage_shadow55.fx" or "#pikes_tree_billboard_shadow.fx"));
            if(draw.GetShaderInstance().ShaderGroup=="#batched_track.fx")continue;
            var reader=new EgoEngineLibrary.Formats.Pssg.RenderDataSourceReader(mesh);if(reader.IndexCount%3!=0 || mesh.Primitive!="triangles")throw new InvalidDataException("Invalid visual primitive.");
            for(int i=0;i<reader.IndexCount;i++)if(!Kn5.Finite(reader.GetPosition(reader.GetIndex(i))))throw new InvalidDataException("Invalid visual position.");
            string material=draw.Shader[1..];
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
    }
}
