using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

internal static class PikesPresentation
{
    internal static void Write(string rally,PikesSourceProfile profile,string output)
    {
        string destination=Path.Combine(output,"replay_camera_config.xml");
        var original=Read(destination);var root=original.Root!;
        string nativeInitial=(string)root.Attribute("initialCamera")!;
    var route=Read(profile.PathIn(rally,"route_0/progress_track.xml"));
    int start=(int)route.Descendants("split").Single(n=>(string?)n.Attribute("type")=="start").Attribute("gate")!;
    var gate=route.Descendants("gate").ElementAt(start);
    var origin=(Parse(gate.Element("left")!.Value)+Parse(gate.Element("right")!.Value))/2-Vector3.UnitY*8;
    var src=Read(profile.PathIn(rally,"route_0/replay_camera_config.xml")).Root!;
    var cameras=src.Elements("Camera").ToDictionary(n=>(string)n.Attribute("ident")!);
    var used=src.Descendants("CameraSwitch").Where(n=>Value(n,"switchType")=="replay").Select(n=>Value(n,"cameraName")).ToHashSet();
    string initial=(string)src.Attribute("initialCamera")!; used.Add(initial);
    var template=root.Elements("Camera").Single(n=>(string?)n.Attribute("ident")=="camera_r1_09");
    var staticTemplate=root.Elements("Camera").First(n=>(string?)n.Attribute("type")=="static");
    var requiredPaths=new HashSet<string>();
    foreach(var name in used.Order()) {
        var input=cameras[name]; string type=(string)input.Attribute("type")!;
        if(type is not ("static" or "zoom"))throw new IOException("Unexpected source camera type");
        var camera=new XElement(type=="zoom"?template:staticTemplate);
        camera.SetAttributeValue("ident",Prefix(name)); camera.Attributes().Where(a=>a.Name.LocalName is "sourcePath" or "targetPath").Remove();
        foreach(string attr in new[]{"sourcePath","targetPath"}) {
            string? reference=(string?)input.Attribute(attr);
            if(!string.IsNullOrEmpty(reference)){requiredPaths.Add(reference);camera.SetAttributeValue(attr,Prefix(reference));}
        }
        // Native DiRT 2 has flat parameters. Rally nests its zoom/noise groups.
        foreach(var p in input.Elements("Parameter")) {
            string key=(string)p.Attribute("name")!;
            if(key=="fov") { Set(camera,"Fov",(string)p.Attribute("value")!); Set(camera,"defaultFov",(string)p.Attribute("value")!); continue; }
            var existing=Parameter(camera,key);
            if(existing is not null)existing.ReplaceWith(new XElement(p));
        }
        foreach(var p in input.Element("HandyCam")?.Elements("Parameter")??[]) {
            var existing=Parameter(camera,(string)p.Attribute("name")!); if(existing is not null)existing.ReplaceWith(new XElement(p));
        }
        foreach(var p in input.Element("CameraZoom")?.Elements("Parameter")??[]) {
            string key=(string)p.Attribute("name")!;
            string? mapped=key switch {"defaultFov"=>"defaultFov","dynamicMinFov"=>"minimumFov","dynamicMaxFov"=>"maximumFov","dynamicTargetRadius"=>"radius",_=>null};
            if(mapped is not null && (mapped!="radius" || (float)p.Attribute("value")!>0))Set(camera,mapped,(string)p.Attribute("value")!);
        }
        if(input.Element("CameraZoom") is XElement zoom)Set(camera,"zoomEnabled",float.Parse((string?)Parameter(zoom,"dynamicTargetRadius")?.Attribute("value")??"0",CultureInfo.InvariantCulture)>0?"true":"false");
        // Preserve fixed source framing without adding donor handheld noise.
        Set(camera,"handyCamEnabled","false"); Set(camera,"crashZoomEnabled","false");
        Set(camera,"useWaitTime","false"); Set(camera,"useWaitDistance","false");
        foreach(string key in new[]{"shakeAngle","shakeRadius","shakeScaleX","shakeScaleY","shakeRotationSpeed"})Set(camera,key,"0");
        if(camera.Elements("Parameter").Any(p=>((string?)p.Attribute("name"))?.Equals("pathRelativeToTarget",StringComparison.OrdinalIgnoreCase)==true&&(string?)p.Attribute("value")=="true"))throw new IOException("World camera inherited a relative donor path");
        Translate(Parameter(camera,"Position")!,origin);
        root.Add(camera);
    }
    foreach(string name in requiredPaths.ToArray()) {
        var pathNode=src.Elements("Path").Single(p=>(string?)p.Attribute("ident")==name);
        if(pathNode.Attribute("percentageCurve") is { Value.Length: >0 } curve)requiredPaths.Add(curve.Value);
    }
    foreach(string name in requiredPaths) {
        var pathNode=new XElement(src.Elements("Path").Single(p=>(string?)p.Attribute("ident")==name));
        pathNode.SetAttributeValue("ident",Prefix(name));
        if(pathNode.Attribute("percentageCurve") is { Value.Length: >0 } curve)curve.Value=Prefix(curve.Value);
        // These referenced track cameras are world-space. 'local' means the
        // track coordinate system, not an offset from the camera or vehicle.
        if(src.Elements("Camera").Where(c=>(string?)c.Attribute("sourcePath")==name||(string?)c.Attribute("targetPath")==name)
            .Any(c=>c.Elements("Parameter").Any(p=>((string?)p.Attribute("name"))?.Equals("pathRelativeToTarget",StringComparison.OrdinalIgnoreCase)==true&&(string?)p.Attribute("value")=="true")))throw new IOException("Unexpected relative replay path");
        foreach(var point in pathNode.Elements("Point"))Translate(point,origin);
        root.Add(pathNode);
    }
    root.Elements("ActivationZone").Remove();
    foreach(var zone in src.Elements("TriggerZone")) {
        if((string?)zone.Attribute("shapeType")!="box")throw new IOException("Unsupported trigger shape");
        var trigger=zone.Element("ZoneEvent")!;var statement=trigger.Element("TriggerStatement")!;
        if(Value(statement,"triggerType")!="onEnter"||Value(statement,"statementType")!="alwaysTrue"||Value(trigger,"probability")!="1.000")throw new IOException("Unsupported conditional camera trigger");
        var converted=new XElement("ActivationZone",new XAttribute("type","box"),new XAttribute("ident",Prefix((string)zone.Attribute("identifier")!)));
        foreach(string name in new[]{"size","position","orientation"})converted.Add(new XElement(Parameter(zone,name)!));
        Translate(Parameter(converted,"position")!,origin);
        var cameraSwitch=trigger.Element("CameraSwitch")!; string camera=Value(cameraSwitch,"cameraName");
        // Native target.* selections leave the automatic TV camera mode.
        // Use the native moving follow camera for Rally's onboard inserts,
        // allowing subsequent course cameras to resume (as in the Spa port).
        string reference=Value(cameraSwitch,"switchType")=="replay"?Prefix(camera):"camera_dyn_+1";
        converted.Add(new XElement("EventHandler",new XAttribute("name","entry"),new XElement("ActivateCamera",new XAttribute("ident",reference))));root.Add(converted);
    }
    // Retain the native initial-camera identity and slot as well as replacing
    // its definition. Saved native camera selections can refer to this slot.
    var initialDefinition=new XElement(root.Elements("Camera").Single(c=>(string?)c.Attribute("ident")==Prefix(initial)));
    initialDefinition.SetAttributeValue("ident",nativeInitial);
    root.Elements("Camera").Single(c=>(string?)c.Attribute("ident")==nativeInitial).ReplaceWith(initialDefinition);
    root.SetAttributeValue("initialCamera","camera_dyn_+1");root.SetAttributeValue("route","0");

        var intro=root.Elements("Camera").Single(c=>(string?)c.Attribute("ident")=="intro_r012_01");
        Vector3[] positions=[new(-520,1492,9100),new(-545,1490,9115),new(-570,1488,9130),new(-595,1486,9145)];
        Vector3[] targets=[new(-830,1415,8880),new(-855,1415,8895),new(-880,1415,8910),new(-905,1415,8925)];
        Position(Parameter(intro,"Position")!,positions[0]);
        var direction=targets[0]-positions[0];
        var q=Quaternion.CreateFromYawPitchRoll(MathF.Atan2(direction.X,direction.Z),-MathF.Atan2(direction.Y,MathF.Sqrt(direction.X*direction.X+direction.Z*direction.Z)),0);
        var orientation=Parameter(intro,"orientation")!;
        foreach(var (axis,v) in new[]{("x",q.X),("y",q.Y),("z",q.Z),("w",q.W)})orientation.SetAttributeValue(axis,v.ToString("R",CultureInfo.InvariantCulture));
        Set(intro,"roll","0");Set(intro,"handyCamEnabled","false");Set(intro,"defaultFov","52");
        foreach(var (name,points) in new[]{("intro_spline",positions),("intro_target_spline",targets)})
        {
            var nodes=root.Elements("Path").Single(p=>(string?)p.Attribute("ident")==name).Elements("Point").ToArray();
            if(nodes.Length!=4)throw new IOException("Unexpected summit intro spline");
            for(int i=0;i<nodes.Length;i++)Position(nodes[i],points[i]);
        }
        var ids=root.Elements().Select(n=>(string?)n.Attribute("ident")).Where(n=>n is not null).ToArray();
        if(ids.Distinct().Count()!=ids.Length)throw new IOException("Duplicate camera/path identifier");
        foreach(var reference in root.Descendants("ActivateCamera").Select(n=>(string)n.Attribute("ident")!))
            if(!reference.StartsWith("target.")&&!ids.Contains(reference))throw new IOException("Missing triggered camera");
        PikesGravelBuild.ReplaceXml(original,destination);
        if(!XNode.DeepEquals(original,Read(destination)))throw new IOException("Binary camera roundtrip changed data");
        Files.Json(Path.Combine(output,"presentation.json"),new{Cameras=used.Count,Paths=requiredPaths.Count,Triggers=src.Elements("TriggerZone").Count(),SummitIntro=true});
    }
    static void Position(XElement p,Vector3 value){p.SetAttributeValue("x",value.X);p.SetAttributeValue("y",value.Y);p.SetAttributeValue("z",value.Z);}
static XDocument Read(string path){using var stream=File.OpenRead(path);return XDocument.Parse(new XmlFile(stream).Document.OuterXml);}
static string Prefix(string name)=>"pikes_replay_"+name;
static XElement? Parameter(XElement parent,string name)=>parent.Elements("Parameter").SingleOrDefault(n=>(string?)n.Attribute("name")==name);
static string Value(XElement parent,string name)=>(string)Parameter(parent,name)!.Attribute("value")!;
static void Set(XElement parent,string name,string value){var p=Parameter(parent,name);if(p is not null)p.SetAttributeValue("value",value);}
static Vector3 Parse(string value){var v=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();if(v.Length!=3)throw new IOException("Bad vector");return new(v[0],v[1],v[2]);}
static void Translate(XElement p,Vector3 origin){foreach(var (axis,delta) in new[]{("x",origin.X),("y",origin.Y),("z",origin.Z)})p.SetAttributeValue(axis,((float)p.Attribute(axis)!-delta).ToString("R",CultureInfo.InvariantCulture));}

}
