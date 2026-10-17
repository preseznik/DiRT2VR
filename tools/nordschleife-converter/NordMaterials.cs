using System.Globalization;
using System.Xml.Linq;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg.Elements;

namespace DiRT2VR.Nordschleife;

// Track-specific adapters. Keep Standard output and the other converters intact.
internal sealed class NordMaterials(Scene scene, string game)
{
    readonly Dictionary<string,(XElement Group,XElement Instance,ShaderInputInfo Layout)> templates=[];
    readonly Dictionary<(string,bool),Material> converted=[];
    readonly Dictionary<string,string> opaque=[];
    readonly Dictionary<string,string> nativeTextures=[];
    NordDecals? decals;
    internal readonly List<object> Report=[];
    static string V(params float[] values)=>string.Join(" ",values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
    static float P(Material material,string key,float fallback=0)=>material.Properties.TryGetValue(key,out var p)?p[0]:fallback;

    (XElement Group,XElement Instance,ShaderInputInfo Layout) Template(string shader)
    {
        if(templates.TryGetValue(shader,out var template))return template;
        string path=Files.Inside(game,shader=="terrain_infield.fx" ? "tracks/london/battersea/route_1/routesplit.pssg" : "tracks/baja/baja_iron/objects.pssg");
        scene.Inputs[path]=Files.Hash(path);var file=Files.Pssg(path);var xml=new XElement("root");
        file.GetObject<PssgShaderGroup>(shader.AsMemory()).WriteXml(xml);
        file.Elements<PssgShaderInstance>().First(i=>i.ShaderGroup=="#"+shader).WriteXml(xml);
        template=(xml.Element("SHADERGROUP")!,xml.Element("SHADERINSTANCE")!,ShaderInputInfo.CreateFromPssg(file).Single(l=>l.ShaderGroupId==shader));
        templates.Add(shader,template);return template;
    }
    string Opaque(string name)
    {
        if(opaque.TryGetValue(name,out var existing))return existing;
        var source=scene.Textures[name];using var input=new MemoryStream(source.Dds,false);var dds=new DdsFile(input);
        DdsAlpha.Prepare(dds,true);using var output=new MemoryStream();dds.Write(output);byte[] bytes=output.ToArray();
        string id="nord_coverage_"+source.Hash[..20]+".dds";
        scene.Textures[id]=new(bytes,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        opaque.Add(name,id);return id;
    }
    string NativeTexture(string name)
    {
        if(name is "nord_black" or "nord_white" or "nord_normal")return name;
        if(nativeTextures.TryGetValue(name,out var ready))return ready;
        var source=scene.Textures[name];using var input=new MemoryStream(source.Dds,false);var dds=new DdsFile(input);
        if(dds.header.ddspf.fourCC==0&&dds.header.ddspf.rGBBitCount==24)
        {
            var format=dds.header.ddspf;
            if(format.rBitMask!=0xff0000||format.gBitMask!=0xff00||format.bBitMask!=0xff||format.aBitMask!=0||dds.bdata.Length%3!=0)
                throw new InvalidDataException("Unsupported source RGB24 layout: "+name);
            var rgba=new byte[checked(dds.bdata.Length/3*4)];
            for(int i=0;i<dds.bdata.Length/3;i++){dds.bdata.AsSpan(i*3,3).CopyTo(rgba.AsSpan(i*4,3));rgba[i*4+3]=255;}
            dds.bdata=rgba;dds.header.ddspf.rGBBitCount=32;dds.header.ddspf.aBitMask=0xff000000;
            dds.header.ddspf.flags=DdsPixelFormat.Flags.DDPF_RGB|DdsPixelFormat.Flags.DDPF_ALPHAPIXELS;
            dds.header.pitchOrLinearSize=dds.header.width*4;
        }
        else if((dds.header.ddspf.flags&DdsPixelFormat.Flags.DDPF_LUMINANCE)!=0)DdsAlpha.Prepare(dds,false);
        else {nativeTextures.Add(name,name);return name;}
        using var expanded=new MemoryStream();dds.Write(expanded);byte[] bytes=expanded.ToArray();
        string id="nord_expanded_"+source.Hash[..20]+".dds";
        scene.Textures[id]=new(bytes,System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        nativeTextures.Add(name,id);return id;
    }
    internal Material ConvertMaterial(Mesh mesh)
    {
        var source=mesh.Material;var key=(source.Id,mesh.CastShadows);
        if(converted.TryGetValue(key,out var existing))return existing;
        if(NordDecals.Matches(source))
        {
            var decal=(decals??=new(scene,game)).Convert(source);
            converted.Add(key,decal);
            Report.Add(new{Source=source.Id,Target="decal_ao.fx",SourceShader=source.Shader,
                NativeTerrainDecal=true,SourceCoverageRetained=true,SpecularAndFresnel=false});
            return decal;
        }
        // Other blended surfaces and object-projected multilayers retain their
        // accepted shaders until their distinct UV/blend contracts are adapted.
        if(source.Blend!=0||source.Shader=="ksPerPixelAlpha"||source.Shader=="ksMultilayer_objsp")return source;
        bool ground=source.Shader=="ksMultilayer_fresnel_nm";
        var template=Template(ground?"terrain_infield.fx":"object_simple_dxt5nm.fx");
        var group=new XElement(template.Group);var instance=new XElement(template.Instance);instance.Elements("SHADERINPUT").Remove();
        string shader=(ground?NordGroundShader.Ground:NordObjectShader.Name)+".fx";
        group.SetAttributeValue("id",shader);instance.SetAttributeValue("shaderGroup","#"+shader);
        var textures=new Dictionary<string,string>(source.Textures);
        var constants=new Dictionary<string,string>(StringComparer.Ordinal);
        bool isOpaque=Visuals.IsOpaque(source);
        void Add(string name,string type)
        {
            var d=new XElement("SHADERINPUTDEFINITION",new XAttribute("name",name),new XAttribute("type",type));
            if(type=="constant")d.SetAttributeValue("format","float4");group.Add(d);
        }
        if(ground)
        {
            foreach(string name in new[]{"NordRoadSheen","NordLighting"})Add(name,"constant");
            foreach(string name in new[]{"TNordDetail","TNordDetailA","TNordNormal"})Add(name,"texture");
            string Map(string channel)
            {
                if(!source.Properties.TryGetValue("mult"+channel,out var values))throw new InvalidDataException("Missing ground mapping: "+source.Id);
                float x=channel=="A"?values[1]:values[0],y=channel=="A"?values[2]:values[0];
                return V(x,y,scene.Origin.X*x,scene.Origin.Z*y);
            }
            foreach(var (name,channel) in new[]{("Map1UVScaleAndOffset","A"),("Map2UVScaleAndOffset","R"),("Map3UVScaleAndOffset","G"),("Map4UVScaleAndOffset","B")})constants[name]=Map(channel);
            constants["DetailBlendScale"]=V(P(source,"magicMult",1));
            var normalScale=source.Properties["detailNMMult"];
            // The native material declares float4 even though the pixel shader
            // uses only XY. A short payload desynchronizes EGO's PSSG reader.
            constants["BlendMaskUVScale"]=V(normalScale[1],normalScale[2],0,0);
            constants["NordRoadSheen"]=V(0,24,1,RoadMaterial.Read(source) is null?0:1);
            constants["NordLighting"]=V(P(source,"ksAmbient",.3f)/.3f,P(source,"ksDiffuse",.3f)/.3f,1,mesh.CastShadows?1:0);
            foreach(var d in group.Elements("SHADERINPUTDEFINITION").Where(d=>(string?)d.Attribute("type")=="texture"))
            {
                string name=(string)d.Attribute("name")!;
                textures[name]=name switch {
                    "TDiffuseSpecMap2"=>source.Textures["txDetailR"],"TDiffuseSpecMap3"=>source.Textures["txDetailG"],
                    "TNordDetail"=>source.Textures["txDetailB"],"TNordDetailA"=>source.Textures["txDetailA"],
                    "TBlendMap2"=>source.Textures["txMask"],"TAmbientOcclusion"=>source.Textures["txDiffuse"],
                    "TNordNormal"=>source.Textures["txDetailNM"],_=>"nord_black"};
            }
        }
        else
        {
            if(P(source,"nmObjectSpace")!=0)throw new InvalidDataException("Object-space source normal requires a separate adapter: "+source.Id);
            foreach(string name in NordObjectShader.Constants)Add(name,"constant");
            foreach(string name in NordObjectShader.Textures)Add(name,"texture");
            bool multi=source.Shader.StartsWith("ksPerPixelMultiMap",StringComparison.Ordinal)&&source.Textures.ContainsKey("txMaps");
            string diffuse=source.Textures["txDiffuse"];
            textures["TDiffuseAlphaMap"]=isOpaque?Opaque(diffuse):diffuse;
            textures["TNormalMap"]=source.Textures.GetValueOrDefault("txNormal","nord_normal");
            textures["TSpecularMap"]=multi?source.Textures["txMaps"]:"nord_white";
            textures["TNordSourceDiffuse"]=multi&&P(source,"useDetail")!=0?diffuse:textures["TDiffuseAlphaMap"];
            textures["TNordObjectDetail"]=multi?source.Textures.GetValueOrDefault("txDetail","nord_white"):"nord_white";
            constants["NordMaterial"]=V(P(source,"ksSpecular"),P(source,"ksSpecularEXP",1),multi?P(source,"useDetail"):0,P(source,"detailUVMultiplier",1));
            constants["NordFresnel"]=V(multi?P(source,"fresnelC"):0,P(source,"fresnelEXP",1),multi?P(source,"fresnelMaxLevel"):0,P(source,"isAdditive"));
            constants["NordSurface"]=V(multi?1:0,source.AlphaTest?1:0,P(source,"sunSpecular"),P(source,"sunSpecularEXP",1));
            constants["NordLighting"]=V(P(source,"ksAmbient",.3f)/.3f,P(source,"ksDiffuse",.3f)/.3f,isOpaque?1:0,mesh.CastShadows?1:0);
        }
        var definitions=group.Elements("SHADERINPUTDEFINITION").ToArray();
        for(int i=0;i<definitions.Length;i++)
        {
            string name=(string)definitions[i].Attribute("name")!;
            if((string?)definitions[i].Attribute("type")=="texture")
            {
                string texture=NativeTexture(textures.GetValueOrDefault(name,"nord_black"));textures[name]=texture;
                instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",i),new XAttribute("type","texture"),new XAttribute("texture",texture)));
            }
            else if(constants.TryGetValue(name,out var value))
            {
                string format=definitions[i].Attribute("format")!.Value;
                ValidateConstant(format,value,name);
                instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",i),new XAttribute("type","constant"),new XAttribute("format",format),value));
            }
        }
        group.SetAttributeValue("parameterCount",definitions.Length);instance.SetAttributeValue("parameterCount",definitions.Length);
        instance.SetAttributeValue("parameterSavedCount",instance.Elements("SHADERINPUT").Count());
        var layout=ground?new ShaderInputInfo(shader,template.Layout.BlockInputs):new ShaderInputInfo(shader,[new ShaderBlockInputInfo([
            new("Vertex","float3",0,60),new("ST","float2",12,60),new("ST","float2",20,60),new("Normal","float3",28,60),
            new("Tangent","half4",40,60),new("Binormal","half4",48,60),new("Color","uint_color_argb",56,60)])]);
        var result=source with{Id=source.Id+(mesh.CastShadows?"":"_no_shadow"),Shader="ksPerPixel",Textures=textures,Native=new(group,instance,layout,isOpaque)};
        converted.Add(key,result);Report.Add(new{Source=source.Id,Target=shader,mesh.CastShadows,SourceShader=source.Shader,
            GroundLayers=ground?4:0,OriginalRgbNormals=true,OriginalTextureMipmaps=true,
            DetailNormalPending=source.Textures.ContainsKey("txNormalDetail")});
        return result;
    }
    internal static void ValidateConstant(string format,string value,string name)
    {
        int count=format switch{"float"=>1,"float2"=>2,"float3"=>3,"float4"=>4,_=>throw new InvalidDataException("Unsupported Nordschleife material constant format: "+format)};
        var parts=value.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length!=count||parts.Any(p=>!float.TryParse(p,NumberStyles.Float,CultureInfo.InvariantCulture,out float v)||!float.IsFinite(v)))
            throw new InvalidDataException("Nordschleife material constant does not match its declared format: "+name+" ("+format+").");
    }
}
