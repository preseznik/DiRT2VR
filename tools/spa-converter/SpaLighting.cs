using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

// Transfer the supported source controls into DiRT 2's track-local schema.
// The two renderers' tone mapping/sky models are not pixel-identical.
internal static class SpaLighting
{
    internal static void Write(Scene scene,string source,string donor,string output)
    {
        XDocument Read(string path)
        {
            Files.NoLinks(path);scene.Inputs[path]=Files.Hash(path);
            using var stream=File.OpenRead(path);return XDocument.Parse(new XmlFile(stream).Document.OuterXml);
        }
        var day=Read(Files.Inside(source,"tracks/locations/europe/spa_francorchamps/lighting_day.xml"));
        using var receipt=JsonDocument.Parse(File.ReadAllBytes(Files.Inside(source,"source.json")));
        string archive=receipt.RootElement.GetProperty("Source").GetString()!;
        string grid=Directory.GetParent(archive)!.Parent!.Parent!.FullName;
        var sourceEffects=Read(Files.Inside(grid,"postprocess/effects.xml"));
        var lighting=Read(Files.Inside(donor,"lighting.xml"));
        var effects=Read(Files.Inside(donor,"effects.xml"));
        string Value(string name)=>day.Descendants("param").Attributes(name).Single().Value;
        var changes=new List<object>();
        void Set(string name,string value,string from)
        {
            var attribute=lighting.Descendants("param").Attributes(name).Single();
            changes.Add(new{Target=name,Source=from,Before=attribute.Value,After=value});attribute.Value=value;
        }
        string[] direct=["heightangle","rotationangle","skyRotationAngle","spec_heightangle",
            "skySunRigidity","skySunMultiplier","skySunFalloff","HDRRange",
            "backlight_sunscale","backlight_scale","backlight_skyscale","backlight_ambientscale",
            "specular_colour","specular_multiplier","max_specular_scale","envmap_colour","envmapSpecularPower",
            "trackSpecularScale","trackSpecularPower","trackFresnelMin","trackFresnelMax","trackFresnelPower",
            "fogDistance","fogDensityCap","fogColour","fogColourBrightness","fogHazeDistance",
            "fogHazeSunCurvePower","fogHazeColour","fogHazeBrightness","fogHazeColour2","fogHazeBrightness2",
            "fixedao_scale","sao_scale","objectao_scale","dao_scale"];
        foreach(string name in direct)Set(name,Value(name),name);
        foreach(string channel in new[]{"sun","back","sky","ambient"})Set("backlight_"+channel+"colour",Value("backlight_"+channel+"_colour"),"backlight_"+channel+"_colour");
        for(int i=1;i<=3;i++)Set("day_bakedColour"+i,Value("day_BakedColour"+i),"day_BakedColour"+i);
        Set("spec_linked",Value("spec_linked")=="true"?"1":"0","spec_linked");
        Set("spec_rotationangle",Value("rotationangle"),"rotationangle (source has no separate specular azimuth)");
        Set("skyscale",Value("skyGammaOuterScale"),"skyGammaOuterScale (single native sky scale)");
        Set("skygamma",Value("skyGammaOuterGamma"),"skyGammaOuterGamma (single native sky gamma)");
        Set("lightShaft_strength",Value("lightShaft_enabled")=="true"?Value("lightShaft_strength"):"0","lightShaft_enabled / lightShaft_strength");
        foreach(string name in new[]{"fogTopLevel","fogBaseLevel"})Set(name,(float.Parse(Value(name),CultureInfo.InvariantCulture)-scene.Origin.Y).ToString("R",CultureInfo.InvariantCulture),name+" minus route origin Y");
        var sourceRace=sourceEffects.Descendants("Effect").Single(e=>(string?)e.Attribute("name")=="race_base"&&(string?)e.Attribute("id")=="2");
        var targetRace=effects.Descendants("Effect").Single(e=>(string?)e.Attribute("id")=="2");
        var post=new List<object>();
        foreach(string group in new[]{"ToneMap","ColourBalance","BloomFromDsX4","LensFlares"})
        {
            var target=targetRace.Elements("ParameterGroup").Single(g=>(string?)g.Attribute("name")==group);
            foreach(var parameter in sourceRace.Elements("ParameterGroup").Single(g=>(string?)g.Attribute("name")==group).Elements("Param"))
            {
                string name=parameter.Attribute("name")!.Value;
                var match=target.Elements("Param").SingleOrDefault(p=>(string?)p.Attribute("name")==name);
                if(match is null)continue;
                post.Add(new{Group=group,Name=name,Before=match.Value.Trim(),After=parameter.Value.Trim()});match.Value=parameter.Value.Trim();
            }
        }
        // GRID 2's race base has zero bloom mixes. Disable the older renderer's
        // extra streak mix too; it has no matching source bloom control.
        var bloom=targetRace.Elements("ParameterGroup").Single(g=>(string?)g.Attribute("name")=="BloomFromDsX4");
        foreach(string name in new[]{"intensity","mixStreak"})
        {
            var parameter=bloom.Elements("Param").Single(p=>(string?)p.Attribute("name")==name);
            post.Add(new{Group="BloomFromDsX4",Name=name,Before=parameter.Value.Trim(),After="0"});parameter.Value="0";
        }
        foreach(var(name,document) in new[]{("lighting.xml",lighting),("effects.xml",effects)})
        {
            string path=Path.Combine(output,"shared",name);Files.Xml(document,path);
            using var stream=File.OpenRead(path);var saved=XDocument.Parse(new XmlFile(stream).Document.OuterXml);
            foreach(var node in new[]{document,saved}.SelectMany(d=>d.Descendants()).Where(n=>!n.HasElements&&n.Value.Length==0))node.RemoveNodes();
            if(!XNode.DeepEquals(document,saved))throw new InvalidDataException("Spa lighting round trip changed: "+name);
        }
        Files.Json(Path.Combine(output,"lighting.json"),new{Profile="GRID 2 Spa daylight / global race_base",TrackLocal=true,SourceControls=changes,PostProcess=post,
            Approximations=new[]{"Native DiRT 2 sky has one gamma/scale instead of two angular regions","Native DiRT 2 fog and HDR pipeline retained","Source deferred lighting, spherical harmonics and tree shader are not reproduced"},RuntimeValidated=false});
    }
}
