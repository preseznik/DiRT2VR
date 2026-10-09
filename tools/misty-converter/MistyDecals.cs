using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Asphalt overlays need the native terrain decal pass, not glass lighting/sorting.
internal sealed class MistyDecals
{
    readonly XElement group, template;
    readonly ShaderInputInfo layout;
    internal MistyDecals(Scene scene,string game)
    {
        string path=Files.Inside(game,"tracks/croatia/croatia_raid/tracksplit.pssg");
        scene.Inputs[path]=Files.Hash(path);
        var file=Files.Pssg(path);
        layout=ShaderInputInfo.CreateFromPssg(file).Single(l=>l.ShaderGroupId=="decal_ao.fx");
        var xml=new XElement("root");
        file.GetObject<PssgShaderGroup>("decal_ao.fx".AsMemory()).WriteXml(xml);
        file.Elements<PssgShaderInstance>().First(i=>i.ShaderGroup=="#decal_ao.fx").WriteXml(xml);
        group=xml.Element("SHADERGROUP")!;template=xml.Element("SHADERINSTANCE")!;
    }
    internal Material Convert(Material source)
    {
        var instance=new XElement(template);
        var definitions=group.Elements("SHADERINPUTDEFINITION").ToArray();
        var textures=new Dictionary<string,string>(source.Textures,StringComparer.Ordinal);
        for(int i=0;i<definitions.Length;i++)
        {
            string name=(string)definitions[i].Attribute("name")!;
            var input=instance.Elements("SHADERINPUT").SingleOrDefault(e=>(int)e.Attribute("parameterID")! == i);
            if((string?)definitions[i].Attribute("type")=="texture")
            {
                textures[name]=name switch{
                    "TDiffuseAlphaMap"=>source.Textures["txDiffuse"],
                    "TAmbientOcclusionMap"=>"nord_white",
                    "TNormalMap"=>"nord_normal_dxt5",
                    "TSpecularMap"=>"nord_black",
                    _=>throw new InvalidDataException("Unknown decal input: "+name)};
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
