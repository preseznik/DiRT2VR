using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using DiRT2VR.CarImport;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;
namespace DiRT2VR.CustomCars;

internal static class WheelValidation
{
    internal static void Write(string source,string profile,string output)
    {
        if(File.Exists(output))throw new IOException("Preserve wheel validation evidence");
        using var nd2Stream=File.OpenRead(Path.Combine(profile,"f04.nd2"));var damageDoc=new XmlFile(nd2Stream).Document;var wheelLayout=(XmlElement)damageDoc.SelectSingleNode("/DAMAGE/WHEELS")!;
        float WheelValue(string axle,string key)=>float.Parse(wheelLayout.GetAttribute("m_"+axle+"_"+key),CultureInfo.InvariantCulture);
        var original=Hash(source);var model=CarKn5.Read(source);var pivots=new Dictionary<string,Vector3>();
        void Visit(CarNode n,Matrix4x4 world){world=n.LocalTransform*world;var suffix=n.Name switch{"WHEEL_LF"=>"fl","WHEEL_RF"=>"fr","WHEEL_LR"=>"bl","WHEEL_RR"=>"br",_=>null};if(suffix is not null)pivots.Add(suffix,new(world.M41,world.M42,world.M43));foreach(var c in n.Children)Visit(c,world);}
        Visit(model.Root,Matrix4x4.Identity);var checks=new List<object>();
        foreach(var name in new[]{"f04_highLOD.pssg","f04_lowLOD.pssg"})
        {
            using var stream=File.OpenRead(Path.Combine(profile,name));var file=PssgFile.Open(stream);
            foreach(var node in file.Elements<PssgMatrixPaletteJointNode>())
            {
                if(!node.Id.Contains("_wheel_") && !node.Id.Contains("_tyre_"))continue;
                var render=node.ChildElements.OfType<PssgMatrixPaletteJointRenderInstance>().ToArray();if(render.Length==0)continue;
                var side=node.Id[^2..];var t=node.Transform.Transform;var pivot=new Vector3(t.M41,t.M42,t.M43);
                var orientation=t;orientation.Translation=Vector3.Zero;if(orientation!=Matrix4x4.Identity)throw new InvalidDataException("Native animated wheel bind is rotated: "+node.Id);
                if(!pivots.TryGetValue(side,out var sourcePivot) || Vector3.Distance(pivot,sourcePivot)>.005f)throw new InvalidDataException("Native wheel pivot mismatch: "+node.Id);
                var points=new List<Vector3>();foreach(var instance in render){var reader=new RenderDataSourceReader(instance.GetRenderDataSource());for(uint i=instance.StreamOffset;i<instance.StreamOffset+instance.ElementCountFromOffset;i++){points.Add(reader.GetPosition(i));var expected=node.Id.Contains("_tyre_")?new Vector4(1,0,1,1):Vector4.One;if(reader.GetColor(i)!=expected)throw new InvalidDataException("Native wheel colour contract mismatch: "+node.Id);}}
                var min=points.Aggregate(new Vector3(float.MaxValue),Vector3.Min);var max=points.Aggregate(new Vector3(float.MinValue),Vector3.Max);var centre=(min+max)*.5f;
                // A translated mesh would orbit even with a correct native joint.
                if(Math.Abs(centre.Y)>.005f || Math.Abs(centre.Z)>.005f)throw new InvalidDataException("Wheel vertices are off-axis: "+node.Id);
                if(node.Id.Contains("_tyre_") && (Math.Abs((max.Y-min.Y)*.5f-.33f)>.01f || Math.Abs((max.Z-min.Z)*.5f-.33f)>.01f))throw new InvalidDataException("Native tyre radius mismatch: "+node.Id);
                var lod=node.Id[..node.Id.IndexOf('_')];var hubNode=file.Elements<PssgMatrixPaletteJointNode>().SingleOrDefault(j=>j.Id==lod+"_hub_caliper_"+side);
                if(hubNode is not null){var hub=hubNode.Transform.Transform;var axle=side[0]=='f'?"front":"rear";var expectedHub=new Vector3(MathF.CopySign(WheelValue(axle,"x")-WheelValue(axle,"x_from_pivot"),pivot.X),WheelValue(axle,"y")-WheelValue(axle,"wheel_y_offset_from_caliper"),WheelValue(axle,"z")-WheelValue(axle,"wheel_z_offset_from_caliper"));if(Vector3.Distance(new(hub.M41,hub.M42,hub.M43),expectedHub)>.005f)throw new InvalidDataException("Hub/wheel offset mismatch: "+node.Id);}
                else if(lod=="x0")throw new InvalidDataException("Missing primary native hub: "+node.Id);
                checks.Add(new{File=name,Joint=node.Id,Vertices=points.Count,LocalCentre=new[]{centre.X,centre.Y,centre.Z},NativePivot=new[]{pivot.X,pivot.Y,pivot.Z},SourcePivot=new[]{sourcePivot.X,sourcePivot.Y,sourcePivot.Z}});
            }
        }
        if(checks.Count<16)throw new InvalidDataException("Missing rendered wheel and tyre groups");
        using(var stream=File.OpenRead(Path.Combine(profile,"f04.nd2")))
        {
            var doc=new XmlFile(stream).Document;var wheel=(XmlElement)doc.SelectSingleNode("/DAMAGE/WHEELS")!;
            foreach(var (side,sourcePivot) in pivots)
            {
                var axle=side[0]=='f'?"front":"rear";float Num(string key)=>float.Parse(wheel.GetAttribute("m_"+axle+"_"+key),CultureInfo.InvariantCulture);
                if(Vector3.Distance(new(MathF.CopySign(Num("x")-Num("wheel_x_offset_from_disc"),sourcePivot.X),Num("y"),Num("z")),sourcePivot)>.005f)throw new InvalidDataException("Damage wheel centre mismatch");
                var box=(XmlElement)doc.SelectSingleNode("//BOX[@id='Box_wheel_"+side+"']")!;var transform=box.GetAttribute("transform").Split(' ').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                if(transform.Length!=16 || Vector3.Distance(new(transform[12],transform[13],transform[14]),sourcePivot)>.005f)throw new InvalidDataException("Wheel damage box centre mismatch");
            }
        }
        if(Hash(source)!=original)throw new InvalidDataException("Source was changed");
        File.WriteAllText(output,JsonSerializer.Serialize(new{SourceSha256=original,SourcePreserved=true,StaticPivotConsistency=true,NativeRotationAccepted=false,Checks=checks,Limitations=new[]{"Static rest pose and local vertex axis only","Native animated suspension and collision deformation require runtime validation","Box half-extent convention remains inferred"}},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{checks.Count} rendered wheel/tyre groups pass source, hub, local-axis and damage-centre consistency checks.");
    }
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
