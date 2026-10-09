using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Xml;

internal static class MistyAudit
{
    internal static void Run(Scene source,string output)
    {
        using var input=File.OpenRead(Path.Combine(output,"track.jpk"));var jpk=new JpkFile();jpk.Read(input);
        var decoded=new Scene();decoded.Collision.AddRange(TrackGround.Load(jpk).TraverseGrid().SelectMany(c=>c.QuadTree.GetTriangles()));
        var samples=new List<object>();var unsupported=new List<object>();var sourceEdges=new List<object>();float maxHeight=0;
        foreach(var gate in source.Gates.Concat(source.RunoutGates))
        {
            var left=Vector3.Normalize(new Vector3(gate.Tangent.Z,0,-gate.Tangent.X));
            foreach(int side in new[]{-1,0,1})
            {
                float available=side>0?gate.Left:gate.Right;
                float offset=side*MathF.Min(2,MathF.Max(0,available-.25f));
                var position=gate.Position+left*offset;
                float height;string sampledSurface="RDT+";
                try { height=decoded.RoadHeight(position); }
                catch(InvalidDataException)
                {
                    var alternatives=new Dictionary<string,float>();
                    foreach(string surface in source.Surfaces.Keys.Where(k=>k!="RDT+"))
                    {
                        var other=new Scene();other.RoadSurfaces.Clear();other.RoadSurfaces.Add(surface);other.Collision.AddRange(decoded.Collision.Where(t=>t.Material==surface));
                        try{alternatives[surface]=other.RoadHeight(position);}catch(InvalidDataException){}
                    }
                    // Some AC side-width samples extend past the asphalt. Require asphalt
                    // on the driving line; a lateral sample must match original terrain.
                    var original=new Scene();original.Collision.AddRange(source.Collision);
                    bool matched=false;height=0;
                    if(side!=0)foreach(var (surface,y) in alternatives.OrderBy(p=>MathF.Abs(p.Value-position.Y)))
                    {
                        original.RoadSurfaces.Clear();original.RoadSurfaces.Add(surface);
                        try { if(MathF.Abs(original.RoadHeight(position)-y)>.02f)continue; }
                        catch(InvalidDataException){continue;}
                        sampledSurface=surface;height=y;matched=true;
                        sourceEdges.Add(new{gate.Distance,SideMetres=offset,Surface=surface,DecodedY=y,OriginalSurfaceVerified=true});break;
                    }
                    if(!matched){unsupported.Add(new{gate.Distance,SideMetres=offset,Position=new[]{position.X,position.Y,position.Z},Alternatives=alternatives});continue;}
                }
                float error=MathF.Abs(height-position.Y);
                maxHeight=MathF.Max(maxHeight,error);
                if(error>1.5f)throw new InvalidDataException("Road collision is detached from driving line at "+gate.Distance);
                samples.Add(new{gate.Distance,SideMetres=offset,RoadY=height,LineY=position.Y,Surface=sampledSurface});
            }
        }
        if(unsupported.Count!=0)
        {
            Files.Json(Path.Combine(output,"unsupported-road-samples.json"),unsupported);
            throw new InvalidDataException($"{unsupported.Count} samples lack asphalt; see unsupported-road-samples.json.");
        }
        // Query actual decoded asphalt under all four corners of the single-car spawn footprint.
        var start=source.Gates[0];var forward=Vector3.Normalize(new Vector3(start.Tangent.X,0,start.Tangent.Z));
        var sideways=new Vector3(forward.Z,0,-forward.X);var spawn=start.Position-forward*10;
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})decoded.RoadHeight(spawn+forward*(z*2.5f)+sideways*(x*1.2f));
        XDocument Xml(string file){using var s=File.OpenRead(Path.Combine(output,file));return XDocument.Parse(new XmlFile(s).Document.OuterXml);}
        var progress=Xml("progress_track.xml");var ai=Xml("ai_track.xml");
        var gates=progress.Descendants("gate").ToDictionary(g=>(int)g.Attribute("id")!);
        var splits=progress.Descendants("split").ToArray();
        float Distance(XElement gate)=>float.Parse((string)gate.Attribute("distance")!,CultureInfo.InvariantCulture);
        int checkpoints=source.FullCourse?8:3;
        if(gates.Count>256||splits.Length!=(source.FullCourse?9:5)||splits.Count(s=>(string?)s.Attribute("type")=="time")!=checkpoints)throw new InvalidDataException("Unexpected timing inventory.");
        if((string?)splits[0].Attribute("type")!="finish"||splits.Count(s=>(string?)s.Attribute("type")=="finish")!=(source.FullCourse?1:2))throw new InvalidDataException("Incorrect finish identities.");
        var track=progress.Descendants("track").Single();
        if((string?)track.Attribute("type")!=(source.FullCourse?"circuit":"point_to_point"))throw new InvalidDataException("Incorrect route topology.");
        float finish=source.FullCourse?float.Parse((string)track.Attribute("total_distance")!,CultureInfo.InvariantCulture):Distance(gates[(int)splits[^1].Attribute("gate")!])-Distance(gates[(int)splits[0].Attribute("gate")!]);
        if(MathF.Abs(finish-source.Length)>.02f)throw new InvalidDataException("Premature or misplaced finish.");
        var links=ai.Descendants("link").ToArray();
        int aiCount=ai.Descendants("gate").Count();
        if(aiCount>ProgressGates.AiGateBudget||links.Length!=aiCount-(source.FullCourse?0:1))throw new InvalidDataException("Invalid AI circuit inventory.");
        for(int i=0;i<links.Length;i++)
            if((int)links[i].Attribute("from_gate")! != i||(int)links[i].Attribute("to_gate")! != (i+1)%aiCount)throw new InvalidDataException("Broken AI sequence or closing link.");
        if(source.FullCourse&&Vector3.Distance(source.Gates[^1].Position,source.Gates[0].Position)>12)throw new InvalidDataException("Source circuit does not close.");
        var reset=new CQuadTreeFile(File.ReadAllBytes(Path.Combine(output,"resetlines.cqtc"))).GetTriangles().ToArray();
        var recovery=new RouteRecovery(decoded.Collision);var clearance=Vector3.UnitY*6;
        int unsafeFaces=reset.Count(t=>MathF.Max(t.Position0.Y,MathF.Max(t.Position1.Y,t.Position2.Y))>recovery.Floor(t.Position0+clearance,t.Position1+clearance,t.Position2+clearance,t.Position2+clearance)+.03f);
        if(unsafeFaces!=0)throw new InvalidDataException("Recovery planes intersect original road clearance.");
        Files.Json(Path.Combine(output,"prototype-audit.json"),new{Passed=true,DecodedSurfaceSupportSamples=samples.Count,AllCentreSamplesOnAsphalt=true,OriginalRoadEdgeSamples=sourceEdges,MaximumSplineHeightDifferenceMetres=maxHeight,SpawnFootprintCorners=4,ProgressGates=gates.Count,TimingCheckpoints=checkpoints,FinishMetres=finish,RunoutMetres=source.FullCourse?0:source.RunoutGates[^1].Distance-source.Length,UnsafeResetFaces=unsafeFaces,CollisionChunks=jpk.Entries.Count-1,FullCourse=source.FullCourse,RuntimeValidated=false,Samples=samples});
    }
}
