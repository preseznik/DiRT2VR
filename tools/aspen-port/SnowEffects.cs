using System.Globalization;
using System.Xml.Linq;
using EgoEngineLibrary.Xml;

// Snow art from the local DiRT 3 installation, authored through DiRT 2's
// working emitter schema. This is a tuned approximation, not D3 curve parity.
internal static class SnowEffects
{
    internal const string TrackPath = "tracks/usa/d2vr_aspen/aspen-effects/";
    internal static readonly Dictionary<string,string> Surfaces = new()
    {
        ["SNO"]="Powder", ["DSN"]="Powder", ["SWC"]="Packed", ["DSC"]="Packed",
        ["SNB"]="Bank", ["SWS"]="Slush"
    };
    static string F(double n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
    static void Set(XElement node,params (string Name,object Value)[] attributes)
    { foreach(var a in attributes)node.SetAttributeValue(a.Name,a.Value); }
    internal static XDocument CreateParticles(XDocument rear, XDocument front)
    {
        var emitters=new XElement("EmitterLibrary");var systems=new XElement("SystemLibrary");
        int id=19000;
        foreach(string kind in Surfaces.Values.Distinct())
        foreach(bool isFront in new[]{false,true})
        {
            var donor=isFront?front:rear;var system=new XElement(donor.Descendants("System").Single());
            string name="D2VR_Aspen_"+kind+(isFront?"_Front":"");
            Set(system,("id",id++),("name",name));
            int ordinal=0;
            foreach(var birth in system.Descendants("birth"))
            {
                string original=(string)birth.Attribute("emitterID")!;
                var source=donor.Descendants("emitter").Single(n=>(string?)n.Attribute("id")==original);
                bool bits=isFront?ordinal>=3:ordinal==1;
                // Replace the front-wheel pebble meshes with snow-fragment sprites.
                var e=new XElement((string?)source.Element("model")?.Attribute("geometryType")=="sprite"
                    ? source : donor.Descendants("emitter").First(n=>(string?)n.Element("model")?.Attribute("geometryType")=="sprite"));
                Set(e,("id",id),("name",name+"_"+ordinal));birth.SetAttributeValue("emitterID",id++);
                Set(e.Element("model")!, ("geometryType","sprite"),("particleType","litDepth"),("billboardingType","CameraFacing"));
                e.Element("material")!.ReplaceWith(new XElement("material",new XAttribute("textureType","static"),
                    new XAttribute("textureName",bits?"d2vr_aspen_snow_bits.tga":ordinal==0?"d2vr_aspen_snow_wisp.tga":"d2vr_aspen_snow_cloud.tga")));
                double amount=kind switch {"Packed"=>.55,"Bank"=>1.5,"Slush"=>.65,_=>1};
                double life=bits?.35:kind=="Slush"?.45:ordinal==3?1.4:.8;
                Set(e.Element("spawn")!, ("rate",F((bits?28:22)*amount)),("finishWhenStopped","true"));
                foreach(var force in e.Elements("force").ToArray())force.Remove();
                e.Add(new XElement("force",new XAttribute("type","gravity"),new XAttribute("influence",bits?"50":"12")),
                    new XElement("force",new XAttribute("type","drag"),new XAttribute("influence","100"),new XAttribute("rangeX",bits?"60":"350")));
                var p=e.Element("particleProperties")!;
                Set(p.Element("life")!, ("life",F(life)),("variance","25"));
                Set(p.Element("speed")!, ("speed",bits?"5":"2"),("variation","1"),("inheritamount",bits?"0.6":"0.9"));
                Set(p.Element("agescale")!, ("initialWidth",bits?"0.07":"0.18"),("initialHeight",bits?"0.09":"0.22"),
                    ("finalWidth",bits?"0.13":kind=="Slush"?"0.65":"1.2"),("finalHeight",bits?"0.16":kind=="Slush"?"0.7":"1.0"));
                Set(p.Element("alpha")!, ("start",bits?"0.8":"0.32"),("middle",bits?"0.65":"0.22"),("end","0"),("inTime","0.05"),("outTime",F(life*.8)));
                Set(p.Element("colour")!, ("start",kind=="Slush"?"0.78 0.82 0.85":"0.94 0.96 0.98"),("end","0.9 0.93 0.96"));
                emitters.Add(e);ordinal++;
            }
            systems.Add(system);
        }
        return new XDocument(new XElement("ParticleData",emitters,systems));
    }
    internal static XDocument CreateOverrides(XDocument original,XDocument materials,IEnumerable<string> codes)
    {
        var result=new XDocument(original);
        foreach(string code in codes)
        {
            if(!Surfaces.TryGetValue(code[..3],out string? kind))continue;
            string donor=SnowSurfaces.Recipes[code[..3]].Donor;
            // Particle names differ from collision codes; use the donor surface mapping.
            string name=donor switch {"LDG"=>"LIGHTDRYGRAV","GBK"=>"MEDDRYGRAV","MWG"=>"MEDWETGRAV",_=>throw new InvalidDataException("Unknown snow effect donor.")};
            var particle=new XElement(materials.Descendants("PARTICLE").Single(n=>(string?)n.Attribute("name")==name));
            var effect=particle.Element("kickup")!.Element("effect")!;
            Set(effect,("name","D2VR_Aspen_"+kind),("frontWheelName","D2VR_Aspen_"+kind+"_Front"));
            effect.Attribute("AIName")?.Remove(); // AI is outside the laboratory's supported mode.
            if(result.Root!.Elements("MATERIAL").Any(n=>(string?)n.Attribute("name")==code))throw new InvalidDataException("Duplicate effect override.");
            result.Root.Add(new XElement("MATERIAL",new XAttribute("name",code),particle));
        }
        return result;
    }
    internal static void Build(string track,string d3,string d2,string session,Dictionary<string,string>? inputs=null,AspenLayout? layout=null)
    {
        string trackPath=(layout ?? AspenLayout.Lakeside).EffectsPath;
        XDocument Read(string path){if(inputs is not null)inputs[path]=PortFiles.Hash(path);return PortFiles.ReadXml(path);}
        var rear=Read(Path.Combine(d2,"effects/particle_systems/K_Grav.xml"));
        var front=Read(Path.Combine(d2,"effects/particle_systems/K_Grav_Frnt.xml"));
        var directory=Path.Combine(track,"aspen-effects");Directory.CreateDirectory(directory);
        var particles=CreateParticles(rear,front);
        PortFiles.WriteXml(particles,Path.Combine(directory,"snow.xml"),XmlType.BinXml);
        var texturePath=Path.Combine(d3,"effects/textures/pfx_snow_textures.pssg");
        if(inputs is not null)inputs[texturePath]=PortFiles.Hash(texturePath);
        var textures=PortFiles.ReadPssg(texturePath);
        var textureNames=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            ["k_snow_bits.tga"]="d2vr_aspen_snow_bits.tga",["k_snowwisp2.tga"]="d2vr_aspen_snow_wisp.tga",["k_snow_singlecloud.tga"]="d2vr_aspen_snow_cloud.tga" };
        int found=0;
        foreach(var t in textures.Descendants("TEXTURE").ToArray()) {
            if(textureNames.TryGetValue((string)t.Attribute("id")!,out var name)){t.SetAttributeValue("id",name);found++;}
            else t.Remove();
        }
        if(found!=3)throw new InvalidDataException("Missing source snow textures.");
        PortFiles.WritePssg(textures,Path.Combine(directory,"snow-textures.pssg"));
        var overridePath=Path.Combine(track,"pfx_material_override.xml");
        var codes=PortFiles.ReadXml(Path.Combine(Path.GetDirectoryName(track)!,"surface_materials.xml")).Descendants("MATERIAL")
            .Select(n=>(string)n.Attribute("name")!).Where(n=>n.Length==4 && Surfaces.ContainsKey(n[..3])).ToArray();
        var overrides=CreateOverrides(PortFiles.ReadXml(overridePath),Read(Path.Combine(d2,"effects/particleMaterials.xml")),codes);
        PortFiles.WriteXml(overrides,overridePath+".tmp",XmlType.BinXml);File.Move(overridePath+".tmp",overridePath,true);
        Directory.CreateDirectory(Path.Combine(session,"effects"));
        var entries=new List<EffectsTransaction.Entry>();
        foreach(var file in EffectsTransaction.Targets) {
            string original=Path.Combine(d2,file),target=Path.Combine(session,file);var dataset=Read(original);
            if(file.EndsWith("pfx_kickup_data_set.xml",StringComparison.Ordinal))
                dataset.Root!.Add(new XElement("xml",new XAttribute("processor","ParticleManagerPlugin"),new XAttribute("filename",trackPath+"snow.xml"),new XAttribute("map","RENDER"),new XAttribute("pool","TEMPORARY"),new XAttribute("userdata","pfxeffect")));
            else dataset.Root!.Add(new XElement("pssg",new XAttribute("processor","ParticleManagerPlugin"),new XAttribute("filename",trackPath+"snow-textures.pssg"),new XAttribute("userdata","textures")));
            // Use the same plain-text encoding as these stock registration files.
            using(var writer=new StreamWriter(new FileStream(target,FileMode.CreateNew),new System.Text.UTF8Encoding(false)))dataset.Save(writer);
            entries.Add(new(file,PortFiles.Hash(original),PortFiles.Hash(target)));
        }
        PortFiles.Json(Path.Combine(session,"effects.json"),new EffectsTransaction.Manifest(1,entries.ToArray()));
        PortFiles.Json(Path.Combine(Path.GetDirectoryName(track)!,"snow-effects.json"),new { Systems=particles.Descendants("System").Count(),Emitters=particles.Descendants("emitter").Count(),Textures=3,SurfaceCodes=codes,SourceArt="DiRT 3",EmitterSchema="DiRT 2",HandlingChanged=false,RuntimeValidated=false });
    }
}
