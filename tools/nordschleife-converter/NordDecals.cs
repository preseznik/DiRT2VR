using System.Xml.Linq;
using System.Numerics;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

namespace DiRT2VR.Nordschleife;

// Road paint must use terrain lighting and ordering, rather than glass reflections.
internal sealed class NordDecals
{
    internal const string SourcePath="tracks/croatia/croatia_raid/tracksplit.pssg";
    readonly XElement group,template;
    readonly ShaderInputInfo layout;
    internal NordDecals(Scene scene,string game)
    {
        string path=Files.Inside(game,SourcePath);scene.Inputs[path]=Files.Hash(path);
        var file=Files.Pssg(path);var xml=new XElement("root");
        layout=ShaderInputInfo.CreateFromPssg(file).Single(l=>l.ShaderGroupId=="decal_ao.fx");
        file.GetObject<PssgShaderGroup>("decal_ao.fx".AsMemory()).WriteXml(xml);
        file.Elements<PssgShaderInstance>().First(i=>i.ShaderGroup=="#decal_ao.fx").WriteXml(xml);
        group=xml.Element("SHADERGROUP")!;template=xml.Element("SHADERINSTANCE")!;
    }
    internal static bool Matches(Material source)=>source.Blend==1&&source.Name is
        "graffiti1" or "graffiti2" or "graffiti2_clean" or "graffiti4" or "graffiti5" or "scritte3" or
        "whiteline" or "groove" or "line end" or "tyres add" or "tyres add2";

    internal static Mesh Attributes(Mesh mesh)
    {
        float alpha=mesh.Material.Properties.TryGetValue("alpha",out var value)?value[0]:1;
        if(!float.IsFinite(alpha)||alpha is <0 or >1)throw new InvalidDataException("Invalid Nordschleife decal opacity: "+mesh.Material.Id);
        var colours=mesh.Colors?.Select(c=>new Vector4(c.X,c.Y,c.Z,alpha)).ToArray()??
            Enumerable.Repeat(new Vector4(1,1,1,alpha),mesh.Positions.Length).ToArray();
        return mesh with{Colors=colours};
    }

    internal Material Convert(Material source)
    {
        var instance=new XElement(template);var definitions=group.Elements("SHADERINPUTDEFINITION").ToArray();
        var textures=new Dictionary<string,string>(source.Textures,StringComparer.Ordinal);
        for(int i=0;i<definitions.Length;i++)
        {
            string name=definitions[i].Attribute("name")!.Value;
            var input=instance.Elements("SHADERINPUT").SingleOrDefault(e=>(int)e.Attribute("parameterID")! == i);
            if((string?)definitions[i].Attribute("type")=="texture")
            {
                textures[name]=name switch{
                    "TDiffuseAlphaMap"=>source.Textures["txDiffuse"],"TAmbientOcclusionMap"=>"nord_white",
                    "TNormalMap"=>"nord_normal_dxt5","TSpecularMap"=>"nord_black",
                    _=>throw new InvalidDataException("Unknown Nordschleife decal input: "+name)};
                input?.Remove();
                instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",i),new XAttribute("type","texture"),new XAttribute("texture",textures[name])));
            }
            else if(name is "FresnelMin" or "FresnelMax" or "SpecScale" or "EnvironmentColour" or "PhongColour")
            {
                input?.Remove();
                instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",i),new XAttribute("type","constant"),
                    new XAttribute("format",definitions[i].Attribute("format")!.Value),name.EndsWith("Colour",StringComparison.Ordinal)?"0 0 0":"0"));
            }
        }
        instance.SetAttributeValue("parameterSavedCount",instance.Elements("SHADERINPUT").Count());
        return source with{Textures=textures,Native=new(group,instance,layout,false)};
    }
}
