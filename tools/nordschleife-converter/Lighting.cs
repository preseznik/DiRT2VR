using System.Xml.Linq;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Nordschleife;

internal static class Lighting
{
    internal static readonly Dictionary<string,string> Daylight=new(StringComparer.Ordinal)
    {
        ["backlight_suncolour"]="1.25 1.22 1.18",["backlight_sunscale"]="1.25",
        ["backlight_backcolour"]="0.12 0.14 0.17",["specular_colour"]="1.0 0.98 0.94",
        ["specular_multiplier"]="1.0",["max_specular_scale"]="1.0",
        ["day_bakedColour1"]="1.15 1.12 1.08",["pfx_suncolour"]="1.0 0.98 0.94",
        ["fogColour"]="0.70 0.75 0.81",["fogColourBrightness"]="0.75",
        ["fogDistance"]="800.0",["fogDensityCap"]="0.12",
        ["fogHazeBrightness"]="0.75",["fogHazeColour2"]="0.90 0.95 1.0",
        ["fogHazeBrightness2"]="0.75",["lightShaft_strength"]="0.20"
    };
    internal static readonly Dictionary<(string Group,string Name),string> PostProcess=new()
    {
        [("ToneMap","targetLuminance")]="0.65",[("BloomFromDsX4","intensity")]="0.25",
        [("ColourBalance","tintAmount")]="0.0",[("ColourBalance","contrast")]="1.08",
        [("ColourBalance","brighness")]="0.0",[("ColourBalance","colour")]="1.0",
        [("ColourBalance","vignetteAmount")]="0.20"
    };
    internal static readonly Dictionary<string,string> Overcast=new(StringComparer.Ordinal)
    {
        ["backlight_suncolour"]="0.35 0.36 0.38",["backlight_sunscale"]="0.35",
        ["backlight_skycolour"]="0.42 0.44 0.48",["backlight_ambientcolour"]="0.30 0.32 0.34",
        ["backlight_backcolour"]="0.20 0.22 0.24",["shadow_strength"]="0.35",
        ["skySunMultiplier"]="0.0",["skyscale"]="0.80",["skygamma"]="0.30",
        ["fogColour"]="0.68 0.70 0.73",["fogDensityCap"]="0.18",["lightShaft_strength"]="0.0"
    };
    internal static readonly Dictionary<string,string> Evening=new(StringComparer.Ordinal)
    {
        ["heightangle"]="12.0",["spec_heightangle"]="12.0",["rotationangle"]="270.0",["spec_rotationangle"]="270.0",["skyRotationAngle"]="272.0",
        ["backlight_suncolour"]="1.15 0.78 0.48",["backlight_sunscale"]="0.95",
        ["backlight_skycolour"]="0.25 0.30 0.40",["backlight_ambientcolour"]="0.19 0.23 0.30",
        ["day_bakedColour1"]="0.95 0.84 0.72",["pfx_suncolour"]="0.95 0.70 0.45",
        ["skyscale"]="0.65",["skySunMultiplier"]="40.0",["fogColour"]="0.72 0.64 0.62",
        ["fogHazeColour2"]="1.0 0.78 0.55",["lightShaft_strength"]="0.12"
    };
    internal static void Apply(XDocument lighting,XDocument effects,string condition="Daylight")
    {
        var parametersByName=new Dictionary<string,string>(Daylight,StringComparer.Ordinal);
        var changes=condition switch {"Daylight"=>new Dictionary<string,string>(),"Overcast"=>Overcast,"Evening"=>Evening,_=>throw new InvalidDataException("Unknown Nordschleife conditions.")};
        foreach(var (name,value) in changes)parametersByName[name]=value;
        foreach(var (name,value) in parametersByName)
        {
            var parameters=lighting.Descendants("param").Where(p=>p.Attribute(name) is not null).ToArray();
            if(parameters.Length!=1)throw new InvalidDataException("Missing or duplicate lighting parameter: "+name);
            parameters[0].SetAttributeValue(name,value);
        }
        var effect=effects.Descendants("Effect").Where(e=>(string?)e.Attribute("id")=="2").ToArray();
        if(effect.Length!=1)throw new InvalidDataException("Missing or duplicate track postprocess effect.");
        foreach(var ((group,name),value) in PostProcess)
        {
            var parameters=effect[0].Elements("ParameterGroup").Where(g=>(string?)g.Attribute("name")==group)
                .Elements("Param").Where(p=>(string?)p.Attribute("name")==name).ToArray();
            if(parameters.Length!=1)throw new InvalidDataException("Missing or duplicate track postprocess parameter: "+group+"/"+name);
            parameters[0].Value=value;
        }
    }
    internal static void Write(Scene scene,string donor,string output)
    {
        XDocument Read(string name)
        {
            string path=Files.Inside(donor,name);scene.Inputs[path]=Files.Hash(path);
            using var input=File.OpenRead(path);return XDocument.Parse(new XmlFile(input).Document.OuterXml);
        }
        var lighting=Read("lighting.xml");var effects=Read("effects.xml");Apply(lighting,effects);
        foreach(var (name,document) in new[]{("lighting.xml",lighting),("effects.xml",effects)})
        {
            string path=Path.Combine(output,"shared",name);Files.Xml(document,path);
            using var input=File.OpenRead(path);var saved=XDocument.Parse(new XmlFile(input).Document.OuterXml);
            foreach(var node in new[]{document,saved}.SelectMany(d=>d.Descendants()).Where(n=>!n.HasElements&&n.Value.Length==0))node.RemoveNodes();
            if(!XNode.DeepEquals(document,saved))throw new InvalidDataException("Daylight XML round-trip changed: "+name);
        }
        Files.Json(Path.Combine(output,"lighting.json"),new {Profile="neutral-daylight",Daylight,PostProcess=PostProcess.Select(p=>new{p.Key.Group,p.Key.Name,Value=p.Value}),TrackLocal=true,RuntimeValidated=false});
    }
}
