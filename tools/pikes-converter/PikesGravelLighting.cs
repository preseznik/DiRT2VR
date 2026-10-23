using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class PikesGravelLighting
{
    internal static void Write(string rally,string game,PikesSourceProfile profile,Scene scene,string output)
    {
        string sourcePath=profile.PathIn(rally,"lighting_"+profile.Condition+".xml");
        scene.Inputs[sourcePath]=Files.Hash(sourcePath);
        var source=profile.Lighting(rally);
        var native=PikesSourceProfile.ReadXml(Path.Combine(output,"shared/lighting.xml"));
        var values=source.Descendants("param").SelectMany(p=>p.Attributes()).ToDictionary(a=>a.Name.LocalName,a=>a.Value,StringComparer.Ordinal);
        var copied=new List<string>();
        foreach(var attribute in native.Descendants("param").SelectMany(p=>p.Attributes()))
            if(values.TryGetValue(attribute.Name.LocalName,out string? value)){attribute.Value=value;copied.Add(attribute.Name.LocalName);}
        PikesGravelBuild.ReplaceXml(native,Path.Combine(output,"shared/lighting.xml"));
        source.Save(Path.Combine(output,"source-lighting.xml"));
        string effectPath=Path.Combine(rally,"postprocess/effects.xml");scene.Inputs[effectPath]=Files.Hash(effectPath);
        var sourceEffect=PikesSourceProfile.ReadXml(effectPath).Descendants("Effect")
            .Single(e=>(string?)e.Attribute("name")=="t~usa~"+profile.Venue+"~"+profile.Condition);
        var effects=PikesSourceProfile.ReadXml(Path.Combine(output,"shared/effects.xml"));
        var effect=effects.Descendants("Effect").Single(e=>(string?)e.Attribute("id")=="2");
        var effectChanges=new List<string>();
        foreach(var group in effect.Elements("ParameterGroup"))
        {
            string name=(string)group.Attribute("name")!;
            // Preserve the existing camera/DOF contract. This conversion maps
            // the source's environment grading, not its incompatible DOF ABI.
            if(name.Contains("Depth",StringComparison.OrdinalIgnoreCase)||name.Contains("Dof",StringComparison.OrdinalIgnoreCase))continue;
            var from=sourceEffect.Elements("ParameterGroup").SingleOrDefault(g=>(string?)g.Attribute("name")==name);
            if(from is null)continue;
            foreach(var parameter in group.Elements("Param"))
            {
                string key=(string)parameter.Attribute("name")!;
                var original=from.Elements("Param").SingleOrDefault(p=>(string?)p.Attribute("name")==key || key=="brighness"&&(string?)p.Attribute("name")=="brightness");
                if(original is null)continue;parameter.Value=original.Value.Trim();effectChanges.Add(name+"/"+key);
            }
        }
        PikesGravelBuild.ReplaceXml(effects,Path.Combine(output,"shared/effects.xml"));
        new XDocument(new XElement(sourceEffect)).Save(Path.Combine(output,"source-effects.xml"));
        string palette=sourceEffect.Elements("ParameterGroup").Single(g=>(string?)g.Attribute("name")=="Palette").Element("Param")!.Value.Trim();
        var palettes=PikesSponsorBarrier.Read(Path.Combine(rally,"postprocess/palettes.pssg"));
        var selected=palettes.Elements<PssgTexture>().Where(t=>t.Id==palette||t.Id==palette+".tga").ToArray();
        if(selected.Length!=1)throw new InvalidDataException("Source grading palette missing: "+palette);
        string paletteDirectory=Path.Combine(output,"grading");Directory.CreateDirectory(paletteDirectory);
        using(var stream=File.Create(Path.Combine(paletteDirectory,palette+".dds")))selected[0].ToDdsFile().Write(stream);
        string skyPath=profile.PathIn(rally,"route_0/sky_"+profile.Condition+".pssg");scene.Inputs[skyPath]=Files.Hash(skyPath);
        var sky=Files.Document(PikesSponsorBarrier.Read(skyPath));
        var template=Files.Document(Files.Pssg(Path.Combine(game,"tracks/london/battersea/route_1/sky.pssg")));
        sky.Descendants("SHADERGROUP").Single().ReplaceWith(new XElement(template.Descendants("SHADERGROUP").Single()));
        var shader=sky.Descendants("SHADERINSTANCE").Single();string texture=(string)shader.Element("SHADERINPUT")!.Attribute("texture")!;
        var shaderCopy=new XElement(template.Descendants("SHADERINSTANCE").Single());shaderCopy.SetAttributeValue("id",(string)shader.Attribute("id")!);
        shaderCopy.Elements("SHADERINPUT").Single(e=>(int)e.Attribute("parameterID")! ==0).SetAttributeValue("texture",texture);shader.ReplaceWith(shaderCopy);
        PikesGravelBuild.ReplacePssg(Files.FromDocument(sky),Path.Combine(output,"sky.pssg"));File.Copy(Path.Combine(output,"sky.pssg"),Path.Combine(output,"shared/sky.pssg"),true);
        foreach(string name in new[]{"ambientmaps","envmap"})
        {
            string path=profile.PathIn(rally,name+"_"+profile.Condition+".pssg");scene.Inputs[path]=Files.Hash(path);
            PikesGravelBuild.ReplacePssg(PikesSponsorBarrier.Read(path),Path.Combine(output,"shared",name+".pssg"));
        }
        var overrides=PikesSourceProfile.ReadXml(Path.Combine(output,"route_overrides.xml"));
        overrides.Root!.Element("default")!.SetAttributeValue("track_cull_dist","15000.0");
        overrides.Root.Element("default")!.SetAttributeValue("world_cull_dist","15000.0");
        PikesGravelBuild.ReplaceXml(overrides,Path.Combine(output,"route_overrides.xml"));
        PikesLightingTuning.Apply(profile,output);
        Files.Json(Path.Combine(output,"condition-mapping.json"),new{Source=profile.Venue,profile.Condition,Palette=palette,LightingParameters=copied,GradingParameters=effectChanges,
            SourceOnlyLighting=values.Keys.Except(copied).Order().ToArray(),SourceSkyGeometryAndPixels=true,SourceAmbientAndReflectionMaps=true,RuntimeValidated=false});
    }
}
