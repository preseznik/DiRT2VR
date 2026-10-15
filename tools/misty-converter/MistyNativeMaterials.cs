using System.Globalization;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal sealed class MistyNativeMaterials(Scene scene,string game,bool highQualityAssets=false)
{
    readonly Dictionary<string,(XElement Group,XElement Instance,ShaderInputInfo Layout)> templates=[];
    readonly MistyTexturePacking packing=new(scene,highQualityAssets);
    internal Material Detail(Material source,bool grass=false)
    {
        const string shader="terrain_infield.fx";
        string path=Files.Inside(game,"tracks/london/battersea/route_1/routesplit.pssg");scene.Inputs[path]=Files.Hash(path);
        var file=Files.Pssg(path);var xml=new XElement("root");file.GetObject<PssgShaderGroup>(shader.AsMemory()).WriteXml(xml);
        file.Elements<PssgShaderInstance>().First(i=>i.ShaderGroup=="#"+shader).WriteXml(xml);
        var group=xml.Element("SHADERGROUP")!;var instance=xml.Element("SHADERINSTANCE")!;
        string name=(grass?MistyDetailShader.Grass:MistyDetailShader.Ground)+".fx";
        group.SetAttributeValue("id",name);instance.SetAttributeValue("shaderGroup","#"+name);instance.Elements("SHADERINPUT").Remove();
        foreach(string constant in new[]{"MistyRoadSheen","MistySunTransform","MistyLighting"})group.Add(new XElement("SHADERINPUTDEFINITION",new XAttribute("name",constant),new XAttribute("type","constant"),new XAttribute("format","float4")));
        foreach(string texture in new[]{"TMistyDetail","TMistyDetailA","TMistyNormal","TMistySunMask"})group.Add(new XElement("SHADERINPUTDEFINITION",new XAttribute("name",texture),new XAttribute("type","texture")));
        group.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        instance.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        var textures=new Dictionary<string,string>(source.Textures);
        string Map(string channel)
        {
            var values=source.Properties["mult"+channel];float x=channel=="A"?values[1]:values[0],y=channel=="A"?values[2]:values[0];
            return string.Join(" ",new[]{x,y,scene.Origin.X*x,scene.Origin.Z*y}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        }
        var constants=grass?new Dictionary<string,string>{["Map2UVScaleAndOffset"]=highQualityAssets?"180 0 0 0":"105 0 0 0"}:new(){["Map1UVScaleAndOffset"]=Map("A"),["Map2UVScaleAndOffset"]=Map("R"),["Map3UVScaleAndOffset"]=Map("G"),["Map4UVScaleAndOffset"]=Map("B"),["DetailBlendScale"]=source.Properties["magicMult"][0].ToString("R",CultureInfo.InvariantCulture)};
        if(!grass)constants["BlendMaskUVScale"]=string.Join(" ",source.Properties["detailNMMult"].Skip(1).Take(2).Concat(new[]{0f,0f}).Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        constants["MistyRoadSheen"]=source.Name=="road_asphalt"&&!grass?"0.1 24 1 1":"0 24 0 0"; // Track default matches the 10% slider response.
        constants["MistyLighting"]=highQualityAssets&&!grass?string.Join(" ",new[]{source.Properties["ksAmbient"][0]/.3f,source.Properties["ksDiffuse"][0]/.3f,0,0}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))):"1 1 0 0";
        constants["MistySunTransform"]="0 0 0 0"; // Road world-to-mask transform is set after reading the scene.
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
                    "TMistySunMask"=>source.Name=="road_asphalt"?MistySunOcclusion.TextureName:"nord_black",
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
        bool flat=!source.Textures.ContainsKey("txNormal");
        if(flat?Visuals.IsOpaque(source):!source.AlphaTest&&source.Blend==0)diffuse=packing.Convert(diffuse,"opaque");
        if(highQualityAssets)
        {
            var result=Make(source,"object_simple_dxt5nm.fx",new(){["TDiffuseAlphaMap"]=diffuse,["TNormalMap"]=source.Textures.GetValueOrDefault("txNormal","nord_normal"),["TSpecularMap"]="nord_black"},opaque:flat&&Visuals.IsOpaque(source));
            return HighQuality(source,result,diffuse,MistyObjectShader.Name+".fx");
        }
        return Make(source,"object_simple_dxt5nm.fx",new(){["TDiffuseAlphaMap"]=diffuse,["TNormalMap"]=packing.Convert(source.Textures["txNormal"],"normal"),["TSpecularMap"]="nord_black"});
    }
    Material HighQuality(Material source,Material result,string diffuse,string shader)
    {
        float P(string key,float fallback=0)=>source.Properties.TryGetValue(key,out var p)?p[0]:fallback;
        if(P("nmObjectSpace")!=0)throw new InvalidDataException("HQ materials require source tangent-space normals: "+source.Id);
        bool multi=source.Shader is "ksPerPixelMultiMap" or "ksPerPixelMultiMap_AT";
        var native=result.Native!;var group=new XElement(native.Group);var instance=new XElement(native.Instance);
        group.SetAttributeValue("id",shader);instance.SetAttributeValue("shaderGroup","#"+shader);
        var maps=new Dictionary<string,string>(result.Textures);
        maps["TSpecularMap"]=multi?source.Textures["txMaps"]:"nord_white";
        int spec=Array.FindIndex(group.Elements("SHADERINPUTDEFINITION").ToArray(),d=>(string?)d.Attribute("name")=="TSpecularMap");
        instance.Elements("SHADERINPUT").Single(i=>(int?)i.Attribute("parameterID")==spec).SetAttributeValue("texture",maps["TSpecularMap"]);
        void Add(string name,string type,string value)
        {
            int index=group.Elements("SHADERINPUTDEFINITION").Count();
            var definition=new XElement("SHADERINPUTDEFINITION",new XAttribute("name",name),new XAttribute("type",type));
            var input=new XElement("SHADERINPUT",new XAttribute("parameterID",index),new XAttribute("type",type));
            if(type=="texture"){input.SetAttributeValue("texture",value);maps[name]=value;}
            else {definition.SetAttributeValue("format","float4");input.SetAttributeValue("format","float4");input.Value=value;}
            group.Add(definition);instance.Add(input);
        }
        string V(params float[] p)=>string.Join(" ",p.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        if(!group.Elements("SHADERINPUTDEFINITION").Any(d=>(string?)d.Attribute("name")=="TNormalMap"))Add("TNormalMap","texture",source.Textures.GetValueOrDefault("txNormal","nord_normal"));
        Add("TMistySourceDiffuse","texture",multi&&P("useDetail")!=0?source.Textures["txDiffuse"]:diffuse);
        Add("TMistyObjectDetail","texture",multi?source.Textures["txDetail"]:"nord_white");
        Add("MistyMaterial","constant",V(P("ksSpecular"),P("ksSpecularEXP",1),multi?P("useDetail"):0,P("detailUVMultiplier")));
        Add("MistyFresnel","constant",V(multi?P("fresnelC"):0,P("fresnelEXP",1),multi?P("fresnelMaxLevel"):0,P("isAdditive")));
        Add("MistySurface","constant",V(multi?1:0,source.AlphaTest?1:0,P("sunSpecular"),P("sunSpecularEXP",1)));
        Add("MistyVisibility","constant","0 0 0 0"); // Fail closed until the static scene has been checked.
        // AC and EGO use different light units. Preserve authored material ratios
        // around Misty's common 0.3 reference instead of darkening everything 70%.
        Add("MistyLighting","constant",V(P("ksAmbient",.3f)/.3f,P("ksDiffuse",.3f)/.3f,native.Opaque?1:0,0));
        group.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        instance.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        instance.SetAttributeValue("parameterSavedCount",instance.Elements("SHADERINPUT").Count());
        var layout=shader==MistyObjectShader.Name+".fx"?new ShaderInputInfo(shader,[new ShaderBlockInputInfo([
            new("Vertex","float3",0,60),new("ST","float2",12,60),new("ST","float2",20,60),
            new("Normal","float3",28,60),new("Tangent","half4",40,60),new("Binormal","half4",48,60),new("Color","uint_color_argb",56,60)])]):new(shader,native.Layout.BlockInputs);
        return result with{Textures=maps,Native=new(group,instance,layout,native.Opaque)};
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
    internal Material Boat(Material source)
    {
        var result=Make(source,"object_animated_ripple.fx",
        new(){["TDiffuseAlphaMap"]=packing.Convert(source.Textures["txDiffuse"],"opaque"),["TSpecularMap"]="nord_black",["TEmissiveMap"]="nord_black"},
        // Frequency zero makes every vertex share one displacement: a rigid float,
        // not the bending ripple used for native flags. Native time controls playback.
        new(){["Amplitude"]="0.10",["Frequency"]="0",["EnvironmentColour"]="0 0 0"});
        return highQualityAssets?HighQuality(source,result,result.Textures["TDiffuseAlphaMap"],"object_animated_ripple.fx"):result;
    }

}
