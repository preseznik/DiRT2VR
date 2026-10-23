using System.Text.Json;
using DiRT2VR.Nordschleife;

// Renderer-specific tuning retained from the approved pack. Texture pixels,
// sky geometry and weather maps still come from the selected source condition.
internal static class PikesLightingTuning
{
    sealed record Tuning(Dictionary<string,string> Lighting,Dictionary<string,string> Effects,Dictionary<string,string> Cull);
    internal static void Apply(PikesSourceProfile profile,string output)
    {
        string layout=profile.IsGravel?profile.Condition switch{"day"=>"gravel-day","day_alt"=>"gravel-alt",_=>"gravel-wet"}:"tarmac";
        var tuning=JsonSerializer.Deserialize<Dictionary<string,Tuning>>(PikesResources.Text("lighting-tuning.json"))![layout];
        string lightingPath=Path.Combine(output,"shared/lighting.xml"),effectPath=Path.Combine(output,"shared/effects.xml"),routePath=Path.Combine(output,"route_overrides.xml");
        var lighting=PikesSourceProfile.ReadXml(lightingPath);
        foreach(var (name,value) in tuning.Lighting)
            lighting.Descendants("param").SelectMany(p=>p.Attributes()).Single(a=>a.Name.LocalName==name).Value=value;
        var effects=PikesSourceProfile.ReadXml(effectPath);
        var effect=effects.Descendants("Effect").Single(e=>(string?)e.Attribute("id")=="2");
        foreach(var (key,value) in tuning.Effects)
        {
            var parts=key.Split('/');
            effect.Elements("ParameterGroup").Single(g=>(string?)g.Attribute("name")==parts[0]).Elements("Param").Single(p=>(string?)p.Attribute("name")==parts[1]).Value=value;
        }
        var route=PikesSourceProfile.ReadXml(routePath);
        foreach(var (name,value) in tuning.Cull)route.Root!.Element("default")!.Attribute(name)!.Value=value;
        PikesGravelBuild.ReplaceXml(lighting,lightingPath);PikesGravelBuild.ReplaceXml(effects,effectPath);PikesGravelBuild.ReplaceXml(route,routePath);
        Files.Json(Path.Combine(output,"lighting-tuning.json"),new{Layout=layout,ApprovedLightingParameters=tuning.Lighting.Count,ApprovedEffectParameters=tuning.Effects.Count});
    }
}
