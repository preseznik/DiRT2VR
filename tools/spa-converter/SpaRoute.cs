using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

internal static class SpaRoute
{
    static string F(float n)=>n.ToString("R",CultureInfo.InvariantCulture);
    static Vector3 Parse(string s){var f=s.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();return new(f[0],f[1],f[2]);}
    static XElement Vector(string name,Vector3 p)=>new(name,new XAttribute("format","float3"),$"{F(p.X)} {F(p.Y)} {F(p.Z)}");
    internal static void Write(Scene scene,string source,string donor,string output)
    {
        XDocument Read(string relative)
        {
            string path=Files.Inside(source,"tracks/locations/europe/spa_francorchamps/route_0/"+relative);
            scene.Inputs[path]=Files.Hash(path);using var stream=File.OpenRead(path);
            return XDocument.Parse(new XmlFile(stream).Document.OuterXml);
        }
        var original=Read("progress_track.xml");
        var sourceGates=original.Root!.Element("gates")!.Elements("gate").ToArray();
        var sourceSplits=original.Descendants("split").ToArray();
        var selected=ProgressGates.Indices(scene.Gates,ProgressGates.NativeLimit,sourceSplits.Select(s=>(int)s.Attribute("gate")!)).ToArray();
        var remap=selected.Select((v,i)=>(v,i)).ToDictionary(t=>t.v,t=>t.i);
        var gates=new XElement("gates",new XAttribute("num_gates",selected.Length));
        foreach(int i in selected)
        {
            var gate=new XElement(sourceGates[i]);gate.SetAttributeValue("id",remap[i]);gate.Element("crossing")?.Remove();
            foreach(var point in gate.Elements())point.Value=Vector(point.Name.LocalName,Parse(point.Value)-scene.Origin).Value;
            gates.Add(gate);
        }
        var splits=new XElement(original.Root.Element("routes")!);
        foreach(var split in splits.Descendants("split"))split.SetAttributeValue("gate",remap[(int)split.Attribute("gate")!]);
        // DiRT 2 needs point lines in addition to gate planes. Midpoints lie
        // strictly between the planes, including the closing last-to-first edge.
        var midpoints=new List<Gate>();
        for(int i=0;i<scene.Gates.Length;i++)
        {
            var a=scene.Gates[(i+scene.Gates.Length-1)%scene.Gates.Length];var b=scene.Gates[i];
            float distance=i==0?(a.Distance+b.Distance+scene.Length)/2%scene.Length:(a.Distance+b.Distance)/2;
            midpoints.Add(new((a.Position+b.Position)/2,Vector3.Normalize(a.Tangent+b.Tangent),(a.Left+b.Left)/2,(a.Right+b.Right)/2,distance));
        }
        midpoints=midpoints.OrderBy(g=>g.Distance).ToList();
        if(midpoints.Count>ProgressGates.PointLineBudget)throw new InvalidDataException("Circuit point-line budget exceeded");
        var points=new XElement("points",new XAttribute("num_lines",3));
        foreach(var(name,type,side)in new[]{("progress_centre_line_0","progress",0),("terminal_camera_line_right_r0_00","camera",-1),("terminal_camera_line_left_r0_00","camera",1)})
        {
            var line=new XElement("line",new XAttribute("name",name),new XAttribute("type",type),new XAttribute("num_points",midpoints.Count));
            foreach(var(g,i)in midpoints.Select((g,i)=>(g,i)))
            {
                var left=Vector3.Normalize(new Vector3(g.Tangent.Z,0,-g.Tangent.X));
                line.Add(new XElement("point",new XAttribute("id",i),new XAttribute("distance",F(g.Distance)),Vector("position",g.Position+left*(side>0?g.Left:side<0?-g.Right:0))));
            }
            points.Add(line);
        }
        Files.Xml(new XDocument(new XElement("progress_track_data",new XAttribute("exporter_version","3.0.0"),
            new XElement("track",new XAttribute("type","circuit"),new XAttribute("total_distance",F(scene.Length))),splits,gates,points)),Path.Combine(output,"progress_track.xml"));
        // Keep GRID 2's source racing-line geometry and closed links for recovery.
        // No opponents or source braking model are enabled in this solo prototype.
        var ai=Read("ai_track.xml");var track=ai.Root!.Elements("track").Single(t=>(string?)t.Attribute("name")=="default");
        foreach(var other in ai.Root.Elements("track").Where(t=>t!=track).ToArray())other.Remove();
        foreach(var position in track.Element("gates")!.Elements("gate").Elements("position"))position.Value=Vector("position",Parse(position.Value)-scene.Origin).Value;
        foreach(var gate in track.Element("gates")!.Elements("gate"))if(gate.Element("edges") is null)gate.Add(new XElement("edges",new XAttribute("num_edges",0)));
        foreach(var name in new[]{"brake_lines","min_speed_lines","retire_lines","fork_sets"}){track.Element(name)?.Remove();track.Add(new XElement(name,new XAttribute("num_"+name,0)));}
        track.Element("hold_lines")?.Remove();track.Element("speed_lines")?.Remove();
        Files.Xml(ai,Path.Combine(output,"ai_track.xml"));Files.Xml(ai,Path.Combine(output,"dev_ai_track.xml"));
        using(var input=File.OpenRead(Path.Combine(donor,"route_1/ai_vehicle_track.xml")))
        {
            var vehicle=new XmlFile(input);foreach(System.Xml.XmlElement brake in vehicle.Document.SelectNodes("//brake_lines")!){brake.RemoveAll();brake.SetAttribute("num_brake_lines","0");}
            using var stream=new FileStream(Path.Combine(output,"ai_vehicle_track.xml"),FileMode.CreateNew);vehicle.Write(stream);
        }
        var start=scene.Gates[0];var tangent=Vector3.Normalize(new Vector3(start.Tangent.X,0,start.Tangent.Z));
        var spawn=start.Position-tangent*10;float road=scene.RoadHeight(spawn);spawn.Y=road+.6f;
        var right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,-tangent));var grids=Files.Pssg(Path.Combine(donor,"route_1/grids.pssg"));
        foreach(var node in grids.Elements<PssgNode>())
        {
            node.Transform.Transform=Matrix4x4.Identity;
            if(node.Id.StartsWith("slot_",StringComparison.Ordinal))node.Transform.Transform=new(right.X,right.Y,right.Z,0,0,1,0,0,-tangent.X,-tangent.Y,-tangent.Z,0,spawn.X,spawn.Y,spawn.Z,1);
        }
        Files.Save(grids,Path.Combine(output,"grids.pssg"));
        Files.Xml(new XDocument(new XElement("route",new XElement("default",new XAttribute("track_cull_dist","20000.0"),new XAttribute("world_cull_dist","20000.0"),new XAttribute("track_lod_dist","1200.0"),new XAttribute("fade","10.0")),new XElement("Systems",new XAttribute("tree_settings","low"),new XAttribute("ornament_settings","medium")))),Path.Combine(output,"route_overrides.xml"));
        var recovery=new RouteRecovery(scene.Collision.Where(t=>scene.RoadSurfaces.Contains(t.Material)).Select(t=>t with{Material="RDT+"}));
        SpaBoundaries.Write(scene,source,output);
        foreach(var(name,material)in new[]{("resetlines.cqtc","RESE"),("cameralines.cqtc","RESD")})
        {
            var planes=new QuadTreeMeshDataBuilder(CQuadTreeTypeInfo.Get(CQuadTreeType.Dirt));
            for(int i=0;i<scene.Gates.Length;i++)
            {
                var g=scene.Gates[i];var next=scene.Gates[(i+1)%scene.Gates.Length];
                var left=new Vector3(g.Tangent.Z,0,-g.Tangent.X);var nextLeft=new Vector3(next.Tangent.Z,0,-next.Tangent.X);
                {
                    var a=g.Position+left*(g.Left+35);var b=next.Position+nextLeft*(next.Left+35);var c=next.Position-nextLeft*(next.Right+35);var d=g.Position-left*(g.Right+35);
                    float floor=recovery.Floor(a,b,c,d);a.Y=b.Y=c.Y=d.Y=floor;planes.Add(new(a,b,c,material));planes.Add(new(a,c,d,material));
                }
            }
            File.WriteAllBytes(Path.Combine(output,name),CQuadTreeFile.Create(planes.Build()).Bytes);
        }
        Files.Json(Path.Combine(output,"grid.json"),new{Position=new[]{spawn.X,spawn.Y,spawn.Z},RoadHeight=road,ClearanceMetres=.6f,BehindStartMetres=10,RuntimeValidated=false});
        Files.Json(Path.Combine(output,"progress.json"),new{Circuit=true,FullCourse=true,LengthMetres=scene.Length,SourceGates=scene.Gates.Length,ProgressGates=selected.Length,NativeProgressGateLimit=ProgressGates.NativeLimit,
            CentreLinePoints=midpoints.Count,AiGates=track.Element("gates")!.Elements().Count(),FinishGate=remap[0],TimingGates=sourceSplits.Where(s=>(string?)s.Attribute("type")=="time").Select(s=>remap[(int)s.Attribute("gate")!]),ClosedRecoveryGeometry=true,RuntimeValidated=false});
        Files.Json(Path.Combine(output,"gates.json"),scene.Gates.Select(g=>new{Position=new[]{g.Position.X,g.Position.Y,g.Position.Z},Tangent=new[]{g.Tangent.X,g.Tangent.Y,g.Tangent.Z},g.Distance,g.Left,g.Right}));
    }
}
