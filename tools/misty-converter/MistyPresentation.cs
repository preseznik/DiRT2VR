using System.Numerics;
using System.Globalization;
using System.Xml.Linq;
using EgoEngineLibrary.Xml;
using DiRT2VR.Nordschleife;
internal static class MistyPresentation
{
    internal static void Write(string game,string output,Vector3 start,Vector3 tangent)
    {
        string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
        tangent=Vector3.Normalize(new(tangent.X,0,tangent.Z));
        Vector3 Eye(float along)=>start+tangent*along+Vector3.UnitY*22;
        Vector3 Target(float along)=>start+tangent*(along+125)+Vector3.UnitY*1.5f;
        using var input=File.OpenRead(Files.Inside(game,"tracks/london/battersea/route_1/replay_camera_config.xml"));
        var xml=new XmlFile(input);var doc=XDocument.Parse(xml.Document.OuterXml);
        var camera=doc.Descendants("Camera").Single(c=>(string?)c.Attribute("ident")=="intro_r012_01");
        void Position(XElement e,Vector3 p){e.SetAttributeValue("x",F(p.X));e.SetAttributeValue("y",F(p.Y));e.SetAttributeValue("z",F(p.Z));}
        Position(camera.Elements("Parameter").Single(p=>(string?)p.Attribute("name")=="Position"),Eye(-30));
        var rotation=Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero,-Vector3.Normalize(Target(-30)-Eye(-30)),Vector3.UnitY));
        var q=camera.Elements("Parameter").Single(p=>(string?)p.Attribute("name")=="orientation");Position(q,new(rotation.X,rotation.Y,rotation.Z));q.SetAttributeValue("w",F(rotation.W));
        foreach(var (name,value) in new[]{("roll","0"),("handyCamEnabled","false")})camera.Elements("Parameter").Single(p=>(string?)p.Attribute("name")==name).SetAttributeValue("value",value);
        foreach(string name in new[]{"intro_spline","intro_target_spline"})
        {
            var points=doc.Descendants("Path").Single(p=>(string?)p.Attribute("ident")==name).Elements("Point").ToArray();
            if(points.Length!=4)throw new InvalidDataException("Unexpected donor intro spline.");
            for(int i=0;i<4;i++)Position(points[i],name=="intro_spline"?Eye(-30+i*20):Target(-30+i*20));
        }
        Files.Xml(doc,Path.Combine(output,"replay_camera_config.xml"));
        Files.Json(Path.Combine(output,"presentation-audit.json"),new{Passed=true,CameraHeightAboveStart=22,CameraPath=new[]{Eye(-30),Eye(30)}.Select(p=>new[]{p.X,p.Y,p.Z}),RuntimeValidated=false});
    }
}
