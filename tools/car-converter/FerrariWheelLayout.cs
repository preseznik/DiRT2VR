using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using DiRT2VR.CarImport;
using EgoEngineLibrary.Xml;
namespace DiRT2VR.CustomCars;

internal static class FerrariWheelLayout
{
    internal static void Build(string source,string donor,string output)
    {
        var model=CarKn5.Read(Path.Combine(source,"ferrari_f2004.kn5"));var pivots=new Dictionary<string,Vector3>();
        void Visit(CarNode n,Matrix4x4 world){world=n.LocalTransform*world;if(n.Name.StartsWith("WHEEL_"))pivots.Add(n.Name,new(world.M41,world.M42,world.M43));foreach(var child in n.Children)Visit(child,world);}
        Visit(model.Root,Matrix4x4.Identity);
        var originalPath=Path.Combine(donor,"cars/prc/prc.nd2");using var input=File.OpenRead(originalPath);var xml=new XmlFile(input);var changes=new List<object>();
        void Set(XmlElement node,string name,float value)
        {
            if(!node.HasAttribute(name) || !float.IsFinite(value))throw new InvalidDataException("Unsupported native wheel layout: "+name);
            var old=node.GetAttribute(name);var text=value.ToString("R",CultureInfo.InvariantCulture);node.SetAttribute(name,text);changes.Add(new{Element=node.Name,Attribute=name,Original=old,Converted=text});
        }
        var wheels=(XmlElement)(xml.Document.SelectSingleNode("/DAMAGE/WHEELS")??throw new InvalidDataException("Missing wheels"));
        var info=(XmlElement)(xml.Document.SelectSingleNode("/DAMAGE/GAMEINFO")??throw new InvalidDataException("Missing wheel game info"));
        foreach(var (axle,left,right,width) in new[]{("front","LF","RF",.245f),("rear","LR","RR",.325f)})
        {
            var a=pivots["WHEEL_"+left];var b=pivots["WHEEL_"+right];if(Math.Abs(a.X+b.X)>.0001 || Math.Abs(a.Y-b.Y)>.0001 || Math.Abs(a.Z-b.Z)>.0001)throw new InvalidDataException("Asymmetric supported source wheel layout");
            foreach(var (key,value) in new[]{("x",Math.Abs(a.X)),("y",a.Y),("z",a.Z),("r",.33f),("alloy_r",.18f),("x_from_pivot",0f),("wheel_y_offset_from_caliper",0f),("wheel_z_offset_from_caliper",0f),("wheel_x_offset_from_disc",0f),("wheel_y_offset_from_disc",0f),("wheel_z_offset_from_disc",0f)})
            {
                Set(wheels,"m_"+axle+"_"+key,value);
                var infoKey=key.StartsWith("wheel_") || key=="alloy_r"?key:"wheel_"+key;
                Set(info,"m_"+axle+"_"+infoKey,value);
            }
            Set(wheels,"m_"+axle+"_l_width",width);Set(wheels,"m_"+axle+"_r_width",width);
            foreach(var side in new[]{"l","r"})
            {
                var box=(XmlElement)(xml.Document.SelectSingleNode("//BOX[@id='Box_wheel_"+(axle=="front"?"f":"b")+side+"']")??throw new InvalidDataException("Missing native wheel box"));
                var centre=side=="l"?a:b;box.SetAttribute("size",$"{(width*.5f).ToString("R",CultureInfo.InvariantCulture)} 0.33 0.33");
                box.SetAttribute("transform",string.Join(" ",new[]{1f,0,0,0,0,1,0,0,0,0,1,0,centre.X,centre.Y,centre.Z,1}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
            }
        }
        var suspension=(XmlElement)(xml.Document.SelectSingleNode("/DAMAGE/SUSPENSION")??throw new InvalidDataException("Missing native suspension graph"));
        if(suspension.GetAttribute("m_componentsPerWheel")!="10")throw new InvalidDataException("Unsupported native component graph");
        // Preserve the tested native connection graph. The source arm bind axes
        // are converted in FerrariSuspension; do not guess component semantics.
        var target=Path.Combine(output,"f04.nd2");using(var stream=File.Create(target))xml.Write(stream,xml.Type);
        using(var stream=File.OpenRead(target)){var readback=new XmlFile(stream);if(xml.Document.DocumentElement!.OuterXml!=readback.Document.DocumentElement!.OuterXml)throw new InvalidDataException("Wheel definition readback changed");}
        xml.Document.Save(Path.Combine(output,"f04-nd2-readable.xml"));
        File.WriteAllText(Path.Combine(output,"wheel-layout-build.json"),JsonSerializer.Serialize(new{SourceModelSha256=model.Sha256,DonorNd2Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalPath))),NativeOutputSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))),Changes=changes,NativeWheelRotationValidated=false,Limitations=new[]{"Supported F2004 dimensions only","Body collision and damage still donor approximation","Wheel animation origin requires native retest"}},new JsonSerializerOptions{WriteIndented=true}));
    }
}
