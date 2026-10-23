using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

internal static class PikesSourceAI
{
    static float ProjectionHeight(Scene scene,Vector3 position,int routeIndex)
    {
        if(routeIndex is not (0 or 6))return scene.RoadHeight(position);
        float? closest=null;
        foreach(var t in scene.Collision.Where(t=>scene.RoadSurfaces.Contains(t.Material)))
        {
            var a=t.Position0;var b=t.Position1;var c=t.Position2;float div=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);if(MathF.Abs(div)<1e-6f)continue;
            float u=((b.Z-c.Z)*(position.X-c.X)+(c.X-b.X)*(position.Z-c.Z))/div,v=((c.Z-a.Z)*(position.X-c.X)+(a.X-c.X)*(position.Z-c.Z))/div,w=1-u-v;
            if(MathF.Min(u,MathF.Min(v,w))<-1e-5f)continue;
            float h=u*a.Y+v*b.Y+w*c.Y;if(MathF.Abs(h-position.Y)>12)continue;
            if(closest is null||MathF.Abs(h-position.Y)<MathF.Abs(closest.Value-position.Y))closest=h;
        }
        return closest??throw new InvalidDataException("No original road under the upper-sector guide");
    }
    internal static void Write(Scene scene,string rally,string output,int routeIndex=2,PikesSourceProfile? profile=null)
    {
        if(routeIndex is not (0 or 2 or 4 or 6)||routeIndex==0&&!scene.FullCourse)throw new ArgumentException("Expected a Pikes sector or full course");
        string path=(profile??PikesSourceProfile.Modern).PathIn(rally,$"route_{routeIndex}/ai_track.xml");
        scene.Inputs[path]=Files.Hash(path);
        using var stream=File.OpenRead(path);
        var document=XDocument.Parse(new XmlFile(stream).Document.OuterXml);
        var track=document.Root!.Element("track")!;
        var original=track.Element("gates")!.Elements("gate").ToArray();
        Vector3 Position(XElement gate)=>Parse(gate.Element("position")!.Value);
        Vector3 Centre(XElement gate)=>Position(gate)+Parse(gate.Element("normal")!.Value)*float.Parse((string)gate.Descendants("waypoint").First(w=>(string?)w.Attribute("type")=="racing_line").Attribute("length")!,CultureInfo.InvariantCulture)-scene.Origin;
        int Nearest(Vector3 position)=>Enumerable.Range(0,original.Length).MinBy(i=>Vector3.DistanceSquared(Centre(original[i]),position));
        int first=Nearest(scene.Gates[0].Position-scene.Gates[0].Tangent*20);
        int last=Nearest(scene.RunoutGates[^1].Position);
        if(last<=first||!scene.FullCourse&&last-first+1>ProgressGates.AiGateBudget)throw new InvalidDataException("Source AI sector exceeds the native gate budget");
        var sourceIndices=Enumerable.Range(first,last-first+1).ToArray();
        float maximumHorizontalDeviation=0;
        if(scene.FullCourse)
        {
            if(original.Where((g,i)=>(int)g.Attribute("id")! != i).Any())throw new InvalidDataException("Full source AI IDs are not sequential");
            var required=track.Element("brake_lines")!.Elements().Concat(track.Element("hold_lines")!.Elements())
                .Select(g=>(int)g.Attribute("gate_id")!).Where(i=>i>=first&&i<=last).Select(i=>i-first);
            var guidance=sourceIndices.Select(i=>new Gate(Centre(original[i]),Vector3.UnitZ,1,1,i)).ToArray();
            var retained=ProgressGates.Indices(guidance,ProgressGates.AiGateBudget,required);
            for(int s=0;s+1<retained.Length;s++)
            {
                var a=guidance[retained[s]].Position;var b=guidance[retained[s+1]].Position;
                var edge=new Vector2(b.X-a.X,b.Z-a.Z);
                for(int i=retained[s]+1;i<retained[s+1];i++)
                {
                    var delta=new Vector2(guidance[i].Position.X-a.X,guidance[i].Position.Z-a.Z);
                    float t=edge.LengthSquared()>1e-8f?Math.Clamp(Vector2.Dot(delta,edge)/edge.LengthSquared(),0,1):0;
                    maximumHorizontalDeviation=MathF.Max(maximumHorizontalDeviation,(delta-edge*t).Length());
                }
            }
            if(maximumHorizontalDeviation>.5f)throw new InvalidDataException("Reduced full-course AI deviates more than 0.5 m from source guidance");
            sourceIndices=retained.Select(i=>first+i).ToArray();
        }
        var selected=sourceIndices.Select(i=>new XElement(original[i])).ToArray();
        var gateMap=selected.Select((g,i)=>(Old:(int)g.Attribute("id")!,New:i)).ToDictionary(p=>p.Old,p=>p.New);
        // All 699 middle-sector samples match source/native road within 0.000183 m.
        // Five original guide heights exceed 2 m; the measured maximum is 2.193 m.
        // All 755 upper guides match source/native surfaces within 0.000164 m;
        // original guide heights require at most 4.663 m of vertical projection.
        float projectionLimit=routeIndex==4?2.25f:routeIndex is 0 or 6?4.75f:2;
        float maximumHeightAdjustment=0;
        foreach(var (gate,i) in selected.Select((g,i)=>(g,i)))
        {
            gate.SetAttributeValue("id",i);
            var position=Position(gate)-scene.Origin;
            var centre=Centre(gate);float height=ProjectionHeight(scene,centre,routeIndex);
            float adjustment=height-centre.Y;
            if(!float.IsFinite(height)||MathF.Abs(adjustment)>projectionLimit)throw new InvalidDataException($"Source AI gate {sourceIndices[i]} cannot be projected onto native road: {centre}, height={height}");
            position.Y+=adjustment;maximumHeightAdjustment=MathF.Max(maximumHeightAdjustment,MathF.Abs(adjustment));
            gate.Element("position")!.Value=$"{F(position.X)} {F(position.Y)} {F(position.Z)}";
            var waypoints=gate.Element("waypoints")!;
            foreach(var waypoint in waypoints.Elements().Where(w=>(string?)w.Attribute("type")=="racing_line"&&(string?)w.Element("racing_line")!.Attribute("type")!="optimal").ToArray())waypoint.Remove();
            waypoints.SetAttributeValue("num_waypoints",waypoints.Elements().Count());
            foreach(var (waypoint,id) in waypoints.Elements().Select((w,id)=>(w,id)))waypoint.SetAttributeValue("id",id);
            if(gate.Element("edges") is null)gate.Add(new XElement("edges",new XAttribute("num_edges",0)));
            else if(gate.Element("edges")!.HasElements)throw new InvalidDataException("Unsupported branching source AI gate");
        }
        var brakes=track.Element("brake_lines")!.Elements().Where(b=>gateMap.ContainsKey((int)b.Attribute("gate_id")!)).Select(b=>new XElement(b)).ToArray();
        var holds=track.Element("hold_lines")!.Elements().ToDictionary(h=>(int)h.Attribute("id")!);
        brakes=brakes.Where(b=>holds.TryGetValue((int)b.Element("brake_data")!.Attribute("hold_line_id")!,out var hold)&&gateMap.ContainsKey((int)hold.Attribute("gate_id")!)).ToArray();
        var savedHolds=new List<XElement>();
        var nativeBrakes=new List<object>();
        for(int i=0;i<brakes.Length;i++)
        {
            var brake=brakes[i];var data=brake.Element("brake_data")!;
            var hold=new XElement(holds[(int)data.Attribute("hold_line_id")!]);
            int brakeGate=(int)brake.Attribute("gate_id")!,holdGate=(int)hold.Attribute("gate_id")!;
            float minimum=float.Parse((string)data.Attribute("min_speed")!,CultureInfo.InvariantCulture);
            float maximum=float.Parse((string)data.Attribute("max_speed")!,CultureInfo.InvariantCulture);
            if(holdGate<=brakeGate||minimum<=0||maximum<minimum)throw new InvalidDataException("Unsupported source braking section");
            float distance=Enumerable.Range(brakeGate,holdGate-brakeGate).Sum(g=>Vector3.Distance(Centre(original[g]),Centre(original[g+1])));
            float holdTime=distance/minimum;
            // DiRT 2 parses normal brakes with hold_time, not Rally's hold-line IDs.
            var nativeData=new XElement("brake_data",new XAttribute("type","normal"),new XAttribute("min_speed",F(minimum)),new XAttribute("max_speed",F(maximum)),
                new XAttribute("hold_time",F(holdTime)),new XAttribute("distance_modifier",(string)data.Attribute("distance_modifier")!),new XAttribute("curve_modifier",(string)data.Attribute("curve_modifier")!));
            data.ReplaceWith(nativeData);
            brake.SetAttributeValue("id",i);brake.SetAttributeValue("gate_id",gateMap[brakeGate]);
            hold.SetAttributeValue("id",i);hold.SetAttributeValue("gate_id",gateMap[(int)hold.Attribute("gate_id")!]);hold.SetAttributeValue("brakeline_id",i);savedHolds.Add(hold);
            nativeBrakes.Add(new{Id=i,SourceBrakeGate=brakeGate,SourceHoldGate=holdGate,HoldDistanceMetres=distance,NativeHoldSeconds=holdTime,MinimumSpeed=minimum,MaximumSpeed=maximum});
        }
        track.Element("gates")!.ReplaceWith(new XElement("gates",new XAttribute("num_gates",selected.Length),selected));
        track.Element("links")!.ReplaceWith(new XElement("links",new XAttribute("num_links",selected.Length-1),Enumerable.Range(0,selected.Length-1).Select(i=>new XElement("link",new XAttribute("id",i),new XAttribute("from_gate",i),new XAttribute("to_gate",i+1)))));
        track.Element("brake_lines")!.ReplaceWith(new XElement("brake_lines",new XAttribute("num_brake_lines",brakes.Length),brakes));
        track.Element("hold_lines")!.Remove();
        track.Element("speed_lines")!.Remove();
        foreach(string empty in new[]{"min_speed_lines","retire_lines","fork_sets"})
            if(track.Element(empty)!.HasElements)throw new InvalidDataException("Unsupported nonempty source AI controls: "+empty);
        // These are newly generated files in a fresh candidate, never installed assets.
        foreach(string name in new[]{"ai_track.xml","dev_ai_track.xml"})
        {
            using var xml=new MemoryStream();document.Save(xml);xml.Position=0;
            using var target=new FileStream(Path.Combine(output,name),FileMode.Create);new XmlFile(xml).Write(target,XmlType.BinXml);
        }
        if(Files.Hash(path)!=scene.Inputs[path])throw new InvalidDataException("Source AI changed during conversion");
        var progress=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(output,"progress.json")))!;
        progress["AiGates"]=selected.Length;progress["SourceAiControls"]=true;
        File.WriteAllText(Path.Combine(output,"progress.json"),progress.ToJsonString(new(){WriteIndented=true}));
        Files.Json(Path.Combine(output,"source-ai.json"),new{Source=path,FirstSourceGate=first,LastSourceGate=last,SourceGuidesInRange=last-first+1,RetainedSourceGateIndices=sourceIndices,AiGates=selected.Length,BrakeLines=brakes.Length,SourceHoldLines=savedHolds.Count,HorizontalTranslationOnly=true,GuidanceReduced=scene.FullCourse,MaximumHorizontalDeviationMetres=maximumHorizontalDeviation,ProjectedOntoNativeRoad=true,ProjectionLimitMetres=projectionLimit,MaximumHeightAdjustmentMetres=maximumHeightAdjustment,SourceSpeedTargetsPreserved=true,NativeBrakeType="normal",HoldTimesDerivedFromSourceDistanceAndMinimumSpeed=true,NativeBrakes=nativeBrakes,RuntimeValidated=false});
    }
    static Vector3 Parse(string value){var v=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();return new(v[0],v[1],v[2]);}
    static string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
}
