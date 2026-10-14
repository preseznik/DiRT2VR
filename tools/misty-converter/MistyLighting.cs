using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

internal static class MistyLighting
{
    internal static void WriteVariant(string daylight,string output,string condition)
    {
        using var input=File.OpenRead(daylight);
        var lighting=XDocument.Parse(new XmlFile(input).Document.OuterXml);
        var values=condition switch {
        "Evening"=>new Dictionary<string,string>{
            ["heightangle"]="12",["spec_heightangle"]="12",
            ["backlight_suncolour"]="1.05 0.68 0.42",["backlight_sunscale"]="0.80",
            ["backlight_skycolour"]="0.30 0.36 0.48",["backlight_ambientcolour"]="0.20 0.24 0.31",
            ["backlight_backcolour"]="0.16 0.20 0.27",["day_bakedColour1"]="0.88 0.76 0.64",
            ["pfx_suncolour"]="0.95 0.63 0.40",["specular_colour"]="0.95 0.63 0.40",
            ["skySunMultiplier"]="6",["skyscale"]="0.52",["lightShaft_strength"]="0.035",
            ["fogColour"]="0.57 0.59 0.65",["fogColourBrightness"]="0.65",
            ["fogHazeColour2"]="0.88 0.62 0.43"},
        "Morning Fog"=>new Dictionary<string,string>{
            ["heightangle"]="10",["spec_heightangle"]="10",["rotationangle"]="90",["spec_rotationangle"]="90",["skyRotationAngle"]="92",
            ["backlight_suncolour"]="0.72 0.78 0.86",["backlight_sunscale"]="0.45",
            ["backlight_skycolour"]="0.45 0.51 0.60",["backlight_ambientcolour"]="0.32 0.37 0.43",
            ["backlight_backcolour"]="0.24 0.29 0.35",["day_bakedColour1"]="0.78 0.84 0.92",
            ["pfx_suncolour"]="0.65 0.70 0.78",["specular_colour"]="0.32 0.36 0.42",
            ["skySunMultiplier"]="2",["skyscale"]="0.58",["lightShaft_strength"]="0.02",
            ["fogColour"]="0.62 0.69 0.77",["fogColourBrightness"]="0.85",
            ["fogHazeColour2"]="0.68 0.73 0.81",["fogDistance"]="380",["fogDensityCap"]="0.78"},
        _=>throw new ArgumentException("Unknown Misty lighting preset: "+condition)};
        // Preset light directions must match the static road-occlusion bake.
        foreach(var (key,value) in values)lighting.Descendants("param").Single(p=>p.Attribute(key) is not null).SetAttributeValue(key,value);
        Files.Xml(lighting,output);
    }
    internal static void Write(Scene scene,string donor,string output)
    {
        XDocument Read(string name)
        {
            string path=Files.Inside(donor,name);scene.Inputs[path]=Files.Hash(path);
            using var input=File.OpenRead(path);return XDocument.Parse(new XmlFile(input).Document.OuterXml);
        }
        var lighting=Read("lighting.xml");var effects=Read("effects.xml");Lighting.Apply(lighting,effects);
        var values=new Dictionary<string,string>{
            ["heightangle"]="22",["spec_heightangle"]="22",["rotationangle"]="270",["spec_rotationangle"]="270",["skyRotationAngle"]="272",
            ["backlight_suncolour"]="1.0 0.88 0.72",["backlight_sunscale"]="1.0",["backlight_skycolour"]="0.46 0.52 0.62",
            ["backlight_ambientcolour"]="0.30 0.34 0.37",["backlight_backcolour"]="0.20 0.23 0.28",["day_bakedColour1"]="1.0 0.94 0.84",
            ["skySunMultiplier"]="8",["skyscale"]="0.70",["lightShaft_strength"]="0.08",["fogDistance"]="1800",["fogDensityCap"]="0.12"};
        foreach(var (key,value) in values)lighting.Descendants("param").Single(p=>p.Attribute(key) is not null).SetAttributeValue(key,value);
        var effect=effects.Descendants("Effect").Single(e=>(string?)e.Attribute("id")=="2");
        foreach(var (group,name,value) in new[]{("BloomFromDsX4","intensity","0.10"),("ColourBalance","contrast","1.03"),("ColourBalance","vignetteAmount","0.10")})
            effect.Elements("ParameterGroup").Single(g=>(string?)g.Attribute("name")==group).Elements("Param").Single(p=>(string?)p.Attribute("name")==name).Value=value;
        Files.Xml(lighting,Path.Combine(output,"shared/lighting.xml"));Files.Xml(effects,Path.Combine(output,"shared/effects.xml"));
        Files.Json(Path.Combine(output,"lighting.json"),new{Profile="misty-warm-daylight",Values=values,SourceReference="User source screenshot; visual approximation, not an AC weather/PP import",TrackLocal=true,RuntimeValidated=false});
    }
}
