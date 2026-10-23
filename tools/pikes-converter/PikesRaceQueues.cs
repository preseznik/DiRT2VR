using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Xml;

internal static class PikesRaceQueues
{
    internal static void Write(Scene scene,string game,string candidate,string output,bool gridRace,bool gravel=false)
    {
    var progress=Xml(Path.Combine(candidate,"progress_track.xml"));
    var originalProgress=new XDocument(progress);
    var ai=Xml(Path.Combine(candidate,"ai_track.xml"));
    var originalAi=new XDocument(ai);

    string groundPath=Path.Combine(candidate,"track.jpk");
    using var groundStream=File.OpenRead(groundPath);
    var archive=new JpkFile();archive.Read(groundStream);
    var ground=TrackGround.Load(archive);
    var getCell=typeof(TrackGround).GetMethod("Get",BindingFlags.Instance|BindingFlags.NonPublic,[typeof(int),typeof(int)])!;
    float Height(Vector3 p)
    {
        int x=(int)MathF.Floor((p.X-ground.BoundsMin.X)*65536/(ground.BoundsMax.X-ground.BoundsMin.X));
        int z=(int)MathF.Floor((p.Z-ground.BoundsMin.Z)*65536/(ground.BoundsMax.Z-ground.BoundsMin.Z));
        if(x is <0 or >65535||z is <0 or >65535)throw new IOException("Queue outside ground bounds");
        var cell=(VcQuadTreeFile?)getCell.Invoke(ground,[x,z])??throw new IOException("Missing queue collision cell");
        var matches=new List<float>();
        foreach(var t in cell.GetTriangles())
        {
            if(!new[]{"RDT","SDT","CON","COB","HDG","MDG","LDG","DRT"}.Any(prefix=>t.Material.StartsWith(prefix,StringComparison.Ordinal)))continue;
            var a=t.Position0;var b=t.Position1;var c=t.Position2;
            float div=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);if(MathF.Abs(div)<1e-6f)continue;
            float u=((b.Z-c.Z)*(p.X-c.X)+(c.X-b.X)*(p.Z-c.Z))/div;
            float v=((c.Z-a.Z)*(p.X-c.X)+(a.X-c.X)*(p.Z-c.Z))/div,w=1-u-v;
            if(MathF.Min(u,MathF.Min(v,w))>=-1e-5f)matches.Add(u*a.Y+v*b.Y+w*c.Y);
        }
        if(matches.Count==0)throw new IOException($"No road collision under queue at {p}");
        return matches.MinBy(h=>MathF.Abs(h-p.Y));
    }
    var gates=progress.Root!.Element("gates")!.Elements("gate").ToArray();
    var splits=progress.Descendants("split").ToArray();
    if((string?)splits[0].Attribute("type")!="finish"||(string?)splits[^1].Attribute("type")!="finish"||
       (int)splits[0].Attribute("gate")! != 1)throw new IOException("Unexpected practice progress");
    Vector3 Centre(XElement g)=>(V(g.Element("left")!.Value)+V(g.Element("right")!.Value))/2;
    var start=Centre(gates[1]);
    var forward=Vector3.Normalize(Centre(gates[1])-Centre(gates[0]));forward=Vector3.Normalize(new(forward.X,0,forward.Z));
    var right=new Vector3(-forward.Z,0,forward.X);
    float approach=gridRace?34:30;const float clearance=.6f;
    float oldApproach=Vector2.Distance(new(start.X,start.Z),new(Centre(gates[0]).X,Centre(gates[0]).Z));
    float extension=approach-oldApproach;
    if(extension<9||extension>35)throw new IOException("Unexpected start approach");
    var queue=new List<(Vector3 Position,int Slot)>();
    var supports=new List<object>();
    var road=new List<(Vector3 A,Vector3 B,Vector3 C)>();
    foreach(var mesh in scene.Visuals.Where(m=>gravel?(string?)m.Material.Native?.Group.Attribute("id")=="pikes_bank_road47.fx":m.Material.Name.StartsWith("trk ",StringComparison.Ordinal)))
    for(int k=0;k<mesh.Indices.Length;k+=3){var a=mesh.Positions[mesh.Indices[k]];var b=mesh.Positions[mesh.Indices[k+1]];var c=mesh.Positions[mesh.Indices[k+2]];if(new[]{a,b,c}.All(p=>MathF.Abs(p.X-start.X)<60&&MathF.Abs(p.Z-start.Z)<60))road.Add((a,b,c));}
    bool OnRoad(Vector3 p)=>road.Any(t=>Contains(p,t.A,t.B,t.C));
    const float halfWidth=1.4f,halfLength=3,sideGap=.5f,edgeMargin=.4f;
    var rows=new List<(float Back,float Centre,float Width,int Columns)>();
    for(int row=0;row<4;row++)
    {
        float back=6+row*7.5f;
        var safe=new List<float>();
        for(float lateral=-12;lateral<=12;lateral+=.1f)
        {
            bool valid=true;
            for(float length=-halfLength;length<=halfLength;length+=.25f)
                if(!OnRoad(start-forward*(back-length)+right*lateral)){valid=false;break;}
            if(valid)safe.Add(lateral);
        }
        if(safe.Count==0||safe.Zip(safe.Skip(1)).Any(p=>p.Second-p.First>.11f))throw new IOException("Non-contiguous starting road");
        float width=safe[^1]-safe[0];
        int columns=gridRace?(int)MathF.Floor((width-2*edgeMargin+sideGap)/(2*halfWidth+sideGap)):1;
        if(columns<(gridRace?2:1))throw new IOException($"Reassess grid width: row {row} supports {columns} columns");
        // Preserve the approved four rows of two cars; the gravel mesh also
        // contains broad shoulders, which do not require a wider racing grid.
        rows.Add((back,(safe[0]+safe[^1])/2,width,gridRace?2:1));
    }
    for(int slot=0;slot<(gridRace?8:4);slot++)
    {
        var row=rows[gridRace?slot/2:slot];
        float offset=gridRace?row.Centre+(slot%2==0?-1:1)*(halfWidth+sideGap/2):0;
        var pos=start-forward*row.Back+right*offset;pos.Y=Height(pos)+clearance;
        // Dense road-edge and native collision coverage across the whole footprint.
        for(float lateral=-halfWidth;lateral<=halfWidth+.001f;lateral+=.2f)
        for(float longitudinal=-halfLength;longitudinal<=halfLength;longitudinal+=.25f)
        {
            var sample=pos+right*lateral+forward*longitudinal;
            if(!OnRoad(sample)||MathF.Abs(Height(sample)-(pos.Y-clearance))>.6f)
                throw new IOException("Grid footprint crosses the road edge or unsupported ground");
        }
        // Validate a 2.8 x 6 m car footprint against actual native cell lookup,
        // not merely an overlapping triangle elsewhere in the archive.
        foreach(float lateral in new[]{-1.4f,0,1.4f})foreach(float longitudinal in new[]{-3f,0,3f})
        {
            var point=pos+right*lateral+forward*longitudinal;
            float h=Height(point);
            if(MathF.Abs(h-(pos.Y-clearance))>1.25f)throw new IOException("Queue footprint is not supported by level road");
            supports.Add(new{Slot=slot,Lateral=lateral,Longitudinal=longitudinal,GroundHeight=h});
        }
        queue.Add((pos,slot));
    }
    // Extend only the existing approach before the start, preserving all IDs,
    // checkpoints and the native 256 progress / 1024 AI gate budgets.
    var approachCentre=start-forward*approach;approachCentre.Y=Height(approachCentre);
    var originalFirst=Centre(gates[0]);
    var delta=approachCentre-originalFirst;
    foreach(string edge in new[]{"left","right"})gates[0].Element(edge)!.Value=FV(V(gates[0].Element(edge)!.Value)+delta);
    foreach(var gate in gates.Skip(1))gate.SetAttributeValue("distance",F(N(gate.Attribute("distance")!.Value)+extension));
    // Simultaneous point-to-point starts retain the native grid race finish gate.
    if((string?)splits[0].Attribute("type")!="finish")throw new IOException("Invalid grid race timing");
    if(!gridRace)splits[0].SetAttributeValue("type","start");
    foreach(var line in progress.Root.Element("points")!.Elements("line"))
    {
        var points=line.Elements("point").ToArray();
        var p=V(points[0].Element("position")!.Value)-forward*extension;
        // Camera guide lines may lie off the driving surface; preserve their
        // lateral offset and use the supported approach centre height.
        p.Y=Height(start-forward*(approach+5));
        points[0].Element("position")!.Value=FV(p);
        foreach(var point in points.Skip(1))point.SetAttributeValue("distance",F(N(point.Attribute("distance")!.Value)+extension));
    }
    var track=progress.Root.Element("track")!;
    track.SetAttributeValue("total_distance",F(N(track.Attribute("total_distance")!.Value)+extension));
    var aiGates=ai.Descendants("gate").ToArray();
    if(aiGates.Length!=1024)throw new IOException("Unexpected full-course AI budget");
    var firstAi=aiGates[0];var normal=V(firstAi.Element("normal")!.Value);
    float racingAt=N(firstAi.Descendants("waypoint").Single(w=>(string?)w.Attribute("type")=="racing_line").Attribute("length")!.Value);
    firstAi.Element("position")!.Value=FV(approachCentre-normal*racingAt);
    if(!XNode.DeepEquals(originalAi.Descendants("gate").Skip(1).First(),aiGates[1])||
        !originalAi.Descendants("brake_line").Select(n=>n.ToString()).SequenceEqual(ai.Descendants("brake_line").Select(n=>n.ToString())))
        throw new IOException("Race queue changed course AI controls");
    // Reuse all eight standing slots from the stock grid topology.
    using var stockStream=File.OpenRead(Path.Combine(game,"tracks/baja/baja_rally/route_2/grids.pssg"));
    var stock=PssgFile.Open(stockStream);var stockXml=new XElement("PSSGFILE");stock.RootElement.WriteXml(stockXml);
    var standing=new XElement(stockXml.Descendants("NODE").Single(n=>((string?)n.Attribute("id"))?.StartsWith("grid_start_standing_",StringComparison.Ordinal)==true));
    using var acceptedStream=File.OpenRead(Path.Combine(candidate,"grids.pssg"));
    var accepted=PssgFile.Open(acceptedStream);var gridXml=new XElement("PSSGFILE");accepted.RootElement.WriteXml(gridXml);
    var gridRoot=gridXml.Descendants("ROOTNODE").Single();
    foreach(var node in gridRoot.Elements("NODE").Where(n=>((string?)n.Attribute("id"))?.StartsWith("grid_start_",StringComparison.Ordinal)==true).ToArray())node.Remove();
    gridRoot.Add(standing);
    standing.SetAttributeValue("id","grid_start_standing_route_0");standing.SetAttributeValue("nickname","grid_start_standing_route_0");
    standing.Element("TRANSFORM")!.Value=Matrix(Matrix4x4.Identity);
    foreach(var slot in standing.Elements("NODE").ToArray())
    {
        int index=int.Parse(((string)slot.Attribute("id")!).Split('_')[1],CultureInfo.InvariantCulture);
        if(index>=(gridRace?8:4)){slot.Remove();continue;}
        var p=queue[index].Position;
        var transform=new Matrix4x4(right.X,right.Y,right.Z,0,0,1,0,0,-forward.X,-forward.Y,-forward.Z,0,p.X,p.Y,p.Z,1);
        slot.Element("TRANSFORM")!.Value=Matrix(transform);
    }
    string destination=Path.Combine(output,gridRace?"grid-race":"race");Directory.CreateDirectory(destination);
    SaveXml(progress,Path.Combine(destination,"progress_track.xml"));
    SaveXml(ai,Path.Combine(destination,"ai_track.xml"));SaveXml(ai,Path.Combine(destination,"dev_ai_track.xml"));
    using(var memory=new MemoryStream())
    {
        new XDocument(gridXml).Save(memory);memory.Position=0;
        var stagedGrid=PssgFile.ReadXml(memory,PssgFileType.Pssg);
        using var saved=File.Create(Path.Combine(destination,"grids.pssg"));stagedGrid.Save(saved);
    }

    if(!XNode.DeepEquals(progress,Xml(Path.Combine(destination,"progress_track.xml")))||!XNode.DeepEquals(ai,Xml(Path.Combine(destination,"ai_track.xml"))))throw new IOException("Race route roundtrip changed data");
    Files.Json(Path.Combine(destination,"validation.json"),new{Grid=gridRace,Slots=queue.Count,FootprintSamples=supports.Count,Rows=rows.Select(r=>new{r.Back,r.Centre,r.Width,r.Columns}).ToArray(),ProgressGates=gates.Length,AiGates=aiGates.Length});
    }
    static bool Contains(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        float d=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);if(MathF.Abs(d)<1e-6f)return false;
        float u=((b.Z-c.Z)*(p.X-c.X)+(c.X-b.X)*(p.Z-c.Z))/d,v=((c.Z-a.Z)*(p.X-c.X)+(a.X-c.X)*(p.Z-c.Z))/d;
        return u>=-.00001f&&v>=-.00001f&&u+v<=1.00001f;
    }
static XDocument Xml(string path){using var stream=File.OpenRead(path);return XDocument.Parse(new XmlFile(stream).Document.OuterXml);}
static void SaveXml(XDocument doc,string path){using var memory=new MemoryStream();doc.Save(memory);memory.Position=0;using var file=File.Create(path);new XmlFile(memory).Write(file,XmlType.BinXml);}
static float N(string value)=>float.Parse(value,CultureInfo.InvariantCulture);
static string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
static string FV(Vector3 v)=>$"{F(v.X)} {F(v.Y)} {F(v.Z)}";
static Vector3 V(string text){var p=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(N).ToArray();return new(p[0],p[1],p[2]);}
static string Matrix(Matrix4x4 m)=>string.Join(" ",new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44}.Select(F));

}
