using System.Numerics;
using System.Xml.Linq;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.CustomCars;

internal static class FerrariLayout
{
    // Supported F2004 profile, from the inspected source car.ini DRIVER_EYES_POSITION.
    internal static readonly Vector3 Eyes=new(.000573f,.682822f,-.0663019f);
    static readonly Vector3 DonorEyes=new(.3746f,1.4348f,-.1580f);
    internal static void AdjustAnchors(PssgFile file)
    {
        var driver=file.TryGetObject<PssgNode>("d_bip_position".AsMemory())??throw new InvalidDataException("Missing driver anchor");
        driver.Transform.Transform=Matrix4x4.CreateTranslation(Eyes-DonorEyes);
        foreach(var name in new[]{"D_Lhand_Dummy","D_Rhand_Dummy"})
        {
            var hand=file.TryGetObject<PssgNode>(name.AsMemory())??throw new InvalidDataException("Missing hand anchor");
            var matrix=hand.Transform.Transform;matrix.M41*=.75f;hand.Transform.Transform=matrix;
        }
    }
    internal static void Build(string source,string donor,string output,Matrix4x4 steering)
    {
        using var input=File.OpenRead(Path.Combine(donor,"cars/prc/prc_anim_dummies.pssg"));var anchors=PssgFile.Open(input);
        AdjustAnchors(anchors);
        (anchors.TryGetObject<PssgNode>("steer_master".AsMemory())??throw new InvalidDataException("Missing native steer master")).Transform.Transform=steering;
        using(var target=File.Create(Path.Combine(output,"f04_anim_dummies.pssg")))anchors.WritePssg(target);
        using var cameraInput=File.OpenRead(Path.Combine(donor,"cars/prc/cameras.xml"));var camera=new XmlFile(cameraInput);
        // The upright PRC actor is not retargeted to an F1 seating pose yet.
        // Hide that incompatible actor; independent steering/LCD stay animated.
        foreach(System.Xml.XmlElement node in camera.Document.SelectNodes("//Parameter[@name='DisableCoDriver' or @name='DisableDriver' or @name='DisableDriverHead']")!)node.SetAttribute("value","true");
        var head=camera.Document.SelectSingleNode("//View[@ident='head-cam']")??throw new InvalidDataException("Missing head camera");
        var position=(System.Xml.XmlElement)(head.SelectSingleNode("Parameter[@name='position']")??throw new InvalidDataException("Missing head position"));
        string Number(float v)=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        position.SetAttribute("x",Number(Eyes.X));position.SetAttribute("y",Number(Eyes.Y));position.SetAttribute("z",Number(Eyes.Z));
        ((System.Xml.XmlElement)head.SelectSingleNode("Parameter[@name='nearPlane']")!).SetAttribute("value","0.04");
        ((System.Xml.XmlElement)head.SelectSingleNode("Parameter[@name='tilt']")!).SetAttribute("value","0");
        FerrariCamera.Stabilize(camera.Document);
        using(var target=File.Create(Path.Combine(output,"cameras.xml")))camera.Write(target,camera.Type);
        camera.Document.Save(Path.Combine(output,"cameras-readable.xml"));
        // Rigid bodywork and HR controls are owned by int_f04 in cockpit mode.
        // Only the animated wheels/linkages remain visible from the exterior.
        // The native readout is added independently of source UI graphics and mirrors.
        var config=new XDocument(new XElement("interior_config",new XElement("active_nodes",
            new[]{"fl","fr","bl","br"}.SelectMany(side=>new[]{"wheel","tyre","hub_caliper","disc","wishbone","suspension","suspension2","suspension3"}.Select(part=>"x0_"+part+"_"+side)).Select(n=>new XElement("node",new XAttribute("name",n))))));
        config.Root!.Add(new XElement("displays",new XAttribute("type","digital"),new XElement("display",new XAttribute("value","tach"),new XAttribute("node",FerrariDashboard.RpmNode),new XAttribute("font","int_digi_speed"),new XAttribute("num_chars","5"),new XAttribute("factor",FerrariDashboard.RpmFactor))));
        config.Save(Path.Combine(output,"int_f04.xml"));
    }
}
