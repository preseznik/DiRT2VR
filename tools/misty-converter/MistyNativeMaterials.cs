using System.Globalization;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal sealed class MistyNativeMaterials(Scene scene,string game)
{
    readonly Dictionary<string,(XElement Group,XElement Instance,ShaderInputInfo Layout)> templates=[];
    readonly MistyTexturePacking packing=new(scene);
    internal Material Detail(Material source,bool grass=false)
    {
        const string shader="terrain_infield.fx";
        string path=Files.Inside(game,"tracks/london/battersea/route_1/routesplit.pssg");scene.Inputs[path]=Files.Hash(path);
        var file=Files.Pssg(path);var xml=new XElement("root");file.GetObject<PssgShaderGroup>(shader.AsMemory()).WriteXml(xml);
        file.Elements<PssgShaderInstance>().First(i=>i.ShaderGroup=="#"+shader).WriteXml(xml);
        var group=xml.Element("SHADERGROUP")!;var instance=xml.Element("SHADERINSTANCE")!;
        string name=(grass?MistyDetailShader.Grass:MistyDetailShader.Ground)+".fx";
        group.SetAttributeValue("id",name);instance.SetAttributeValue("shaderGroup","#"+name);instance.Elements("SHADERINPUT").Remove();
        foreach(string texture in new[]{"TMistyDetail","TMistyDetailA","TMistyNormal"})group.Add(new XElement("SHADERINPUTDEFINITION",new XAttribute("name",texture),new XAttribute("type","texture")));
        group.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        instance.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        var textures=new Dictionary<string,string>(source.Textures);
        string Map(string channel)
        {
            var values=source.Properties["mult"+channel];float x=channel=="A"?values[1]:values[0],y=channel=="A"?values[2]:values[0];
            return string.Join(" ",new[]{x,y,scene.Origin.X*x,scene.Origin.Z*y}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        }
        var constants=grass?new Dictionary<string,string>{["Map2UVScaleAndOffset"]="105 0 0 0"}:new(){["Map1UVScaleAndOffset"]=Map("A"),["Map2UVScaleAndOffset"]=Map("R"),["Map3UVScaleAndOffset"]=Map("G"),["Map4UVScaleAndOffset"]=Map("B"),["DetailBlendScale"]=source.Properties["magicMult"][0].ToString("R",CultureInfo.InvariantCulture)};
        if(!grass)constants["BlendMaskUVScale"]=string.Join(" ",source.Properties["detailNMMult"].Skip(1).Take(2).Concat(new[]{0f,0f}).Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        var definitions=group.Elements("SHADERINPUTDEFINITION").ToArray();
        for(int i=0;i<definitions.Length;i++)
        {
            string key=definitions[i].Attribute("name")!.Value;
            if((string?)definitions[i].Attribute("type")=="texture")
            {
                textures[key]=grass?source.Textures["txDiffuse"]:key switch{
                    "TDiffuseSpecMap2"=>source.Textures["txDetailR"],"TDiffuseSpecMap3"=>source.Textures["txDetailG"],
                    "TMistyDetail"=>source.Textures["txDetailB"],"TBlendMap2"=>source.Textures["txMask"],
                    "TMistyDetailA"=>source.Textures["txDetailA"],
                    "TMistyNormal"=>source.Textures["txDetailNM"],
                    "TAmbientOcclusion"=>source.Textures["txDiffuse"],_=>"nord_black"};
                instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",i),new XAttribute("type","texture"),new XAttribute("texture",textures[key])));
            }
            else if(constants.TryGetValue(key,out var value))instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",i),new XAttribute("type","constant"),new XAttribute("format",definitions[i].Attribute("format")!.Value),value));
        }
        instance.SetAttributeValue("parameterSavedCount",instance.Elements("SHADERINPUT").Count());
        return source with{Shader="ksPerPixel",Textures=textures,Native=new(group,instance,new(name,ShaderInputInfo.CreateFromPssg(file).Single(l=>l.ShaderGroupId==shader).BlockInputs),!grass)};
    }
    internal Material DryRoad(Material source)
    {
        var road=RoadMaterial.Read(source)??throw new InvalidDataException("Missing road mapping");
        const string shader="terrain_infield.fx";
        string path=Files.Inside(game,"tracks/london/battersea/route_1/routesplit.pssg");
        scene.Inputs[path]=Files.Hash(path);var file=Files.Pssg(path);
        var xml=new XElement("root");file.GetObject<PssgShaderGroup>(shader.AsMemory()).WriteXml(xml);
        file.Elements<PssgShaderInstance>().First(i=>i.ShaderGroup=="#"+shader).WriteXml(xml);
        var group=xml.Element("SHADERGROUP")!;var instance=xml.Element("SHADERINSTANCE")!;
        var maps=new Dictionary<string,string>(source.Textures,StringComparer.Ordinal);
        foreach(var definition in group.Elements("SHADERINPUTDEFINITION").Where(d=>(string?)d.Attribute("type")=="texture"))
        {
            string name=(string)definition.Attribute("name")!;
            maps[name]=name.StartsWith("TDiffuseSpecMap",StringComparison.Ordinal)?packing.Convert(road.Detail,"matte"):
                name=="TAmbientOcclusion"?packing.Convert(road.Shading,"road-shading"):"nord_black";
            int index=group.Elements("SHADERINPUTDEFINITION").ToList().IndexOf(definition);
            if(!instance.Elements("SHADERINPUT").Any(i=>(int?)i.Attribute("parameterID")==index))
                instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",index),new XAttribute("type","texture"),new XAttribute("texture",maps[name])));
        }
        // Diffuse alpha is specular strength, not coverage, in this native shader.
        // Keep the donor layout/constants and source shading; only change that channel.
        return source with{Textures=maps,Native=new(group,instance,ShaderInputInfo.CreateFromPssg(file).Single(l=>l.ShaderGroupId==shader),true)};
    }
    internal Material Make(Material source,string shader,Dictionary<string,string> textures,Dictionary<string,string>? values=null,bool opaque=false)
    {
        if(!templates.TryGetValue(shader,out var template))
        {
            string donor=shader switch{
                "flag_scrolling.fx"=>"tracks/usa/la_stadium/objects.pssg",
                "object_animated_ripple.fx"=>"tracks/china/china_rally/objects.pssg",
                "terrain_infield_nm.fx"=>"tracks/baja/baja_raid/tracksplit.pssg",
                _=>"tracks/baja/baja_iron/objects.pssg"};
            string path=Files.Inside(game,donor);scene.Inputs[path]=Files.Hash(path);var file=Files.Pssg(path);
            var xml=new XElement("root");file.GetObject<PssgShaderGroup>(shader.AsMemory()).WriteXml(xml);
            file.Elements<PssgShaderInstance>().First(i=>i.ShaderGroup=="#"+shader).WriteXml(xml);
            template=(xml.Element("SHADERGROUP")!,xml.Element("SHADERINSTANCE")!,ShaderInputInfo.CreateFromPssg(file).Single(l=>l.ShaderGroupId==shader));
            templates.Add(shader,template);
        }
        var instance=new XElement(template.Instance);instance.Elements("SHADERINPUT").Remove();
        var definitions=template.Group.Elements("SHADERINPUTDEFINITION").ToArray();
        var maps=new Dictionary<string,string>(source.Textures,StringComparer.Ordinal);
        for(int i=0;i<definitions.Length;i++)
        {
            string name=(string)definitions[i].Attribute("name")!;
            if((string?)definitions[i].Attribute("type")=="texture")
            {
                if(!textures.TryGetValue(name,out var texture))continue;
                maps[name]=texture;
                instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",i),new XAttribute("type","texture"),new XAttribute("texture",texture)));
            }
            else if(values?.TryGetValue(name,out var value)==true)
                instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",i),new XAttribute("type","constant"),new XAttribute("format",definitions[i].Attribute("format")!.Value),value));
        }
        instance.SetAttributeValue("parameterSavedCount",instance.Elements("SHADERINPUT").Count());
        return source with{Textures=maps,Native=new(template.Group,instance,template.Layout,opaque)};
    }
    internal Material Normal(Material source)
    {
        string diffuse=source.Textures["txDiffuse"];
        if(!source.AlphaTest&&source.Blend==0)diffuse=packing.Convert(diffuse,"opaque");
        return Make(source,"object_simple_dxt5nm.fx",new(){["TDiffuseAlphaMap"]=diffuse,["TNormalMap"]=packing.Convert(source.Textures["txNormal"],"normal"),["TSpecularMap"]="nord_black"});
    }
    internal Material Flow(Material source)
    {
        // This native shader supports positive scrolling only. The source's negative V
        // scroll is represented by reversing V and mirroring each texture row below.
        float speed=source.Name switch{"waterfall"=>1.1f,"waterfall2"=>.5f,"beachwaves"=>.1f,_=>throw new InvalidDataException("Unknown flow material")};
        var material=Make(source,"flag_scrolling.fx",new(){["TDiffuseAlphaMap"]=packing.Convert(source.Textures["txDiffuse"],"mirror-v"),["TSpecularMap"]="nord_black",["TOcclusionMap"]="nord_white"},
            new(){["speedU"]="0",["speedV"]=(speed/0.75f).ToString("R",CultureInfo.InvariantCulture),["bounce_scroll"]="0",["numberOfFrames"]="1",["pauseBetweenFrames"]="0",["synchronise"]="1",["EnvironmentColour"]="0 0 0"});
        var native=material.Native!;var group=new XElement(native.Group);var instance=new XElement(native.Instance);
        string name=MistyFlowShader.Name+".fx";group.SetAttributeValue("id",name);instance.SetAttributeValue("shaderGroup","#"+name);
        return material with{Native=new(group,instance,new ShaderInputInfo(name,native.Layout.BlockInputs),false)};
    }
    internal Material Boat(Material source)=>Make(source,"object_animated_ripple.fx",
        new(){["TDiffuseAlphaMap"]=packing.Convert(source.Textures["txDiffuse"],"opaque"),["TSpecularMap"]="nord_black",["TEmissiveMap"]="nord_black"},
        // Frequency zero makes every vertex share one displacement: a rigid float,
        // not the bending ripple used for native flags. Native time controls playback.
        new(){["Amplitude"]="0.10",["Frequency"]="0",["EnvironmentColour"]="0 0 0"});

}
