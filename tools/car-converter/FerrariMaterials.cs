using DiRT2VR.CarImport;
using System.Numerics;
using System.Security.Cryptography;
using System.Xml.Linq;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using SharpGLTF.Materials;

namespace DiRT2VR.CustomCars;

internal sealed record FerrariMaterialSet(Dictionary<int,MaterialBuilder> Materials, List<object> MaterialReport, List<object> TextureReport);
internal static class FerrariMaterials
{
    internal static FerrariMaterialSet Build(CarModel high,string source,PssgFile pssg,PssgTexture textureTemplate,bool interior,HashSet<int> selected)
    {
        var shaders=pssg.Elements<PssgShaderInstance>().ToDictionary(s=>s.Id);
        var shaderLib=pssg.Elements<PssgLibrary>().Single(l=>l.Type=="SHADERINSTANCE");
        var rib=pssg.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");
        var textureIds=new Dictionary<string,string>(StringComparer.Ordinal);var textureReport=new List<object>();
        byte[] SourceTexture(string name)
        {
            var embedded=high.Textures.Single(t=>t.Name==name);
            var overridePath=Path.Combine(source,"skins/00_official",name);
            return File.Exists(overridePath)?File.ReadAllBytes(overridePath):embedded.Data;
        }
        string Texture(string name,CarMaterial? material=null,bool normalMap=false)
        {
            bool bake=material is not null && material.Shader.StartsWith("ksPerPixelMultiMap",StringComparison.Ordinal) && material.Properties.TryGetValue("useDetail",out var enabled) && enabled[0]>0;
            var key=normalMap?"normal:"+name:bake?name+":"+material!.Name:name;
            if(textureIds.TryGetValue(key,out var id))return id;
            var bytes=SourceTexture(name);
            id=(interior?"f04_int_source_":"f04_ext_source_")+textureIds.Count;
            var detail=bake?material!.Textures["txDetail"]:null;
            float scale=bake?material!.Properties["detailUVMultiplier"][0]:0;
            using var data=new MemoryStream(bytes,false);var dds=normalMap?FerrariNormalMap.Pack(bytes):bake?DetailBake.Compose(bytes,SourceTexture(detail!),scale):new DdsFile(data);
            if(dds.header.width==0 || dds.header.height==0 || dds.header.width>8192 || dds.header.height>8192)throw new InvalidDataException("Invalid source texture size");
            bool rawRepacked;try{rawRepacked=RawTexture.Prepare(dds);}catch(InvalidDataException e){throw new InvalidDataException(name+": "+e.Message,e);}
            bool mipMapsAdded=interior && FerrariTextureMipMaps.AddMissing(dds);
            var texture=(PssgTexture)textureTemplate.DeepClone();rib.AppendChild(texture);texture.Id=id;
            dds.ToPssgElement(texture);texture.AutoMipMap=false;
            if(texture.TexelFormat is not ("dxt1" or "dxt3" or "dxt5" or "ui8x4"))throw new InvalidDataException("Unsupported DiRT 2 texture format: "+name+" bits="+dds.header.ddspf.rGBBitCount+" fourCC="+dds.header.ddspf.fourCC+" format="+texture.TexelFormat);
            textureIds.Add(key,id);textureReport.Add(new{Source=name,Id=id,SkinOverride=File.Exists(Path.Combine(source,"skins/00_official",name)),RawRepacked=rawRepacked,MipMapsAdded=mipMapsAdded,NormalPacking=normalMap?"AC red/green to DiRT alpha/green":null,DetailBaked=bake,DetailSource=detail,DetailScale=scale,DetailSha256=detail is null?null:Convert.ToHexString(SHA256.HashData(SourceTexture(detail))),Sha256=Convert.ToHexString(SHA256.HashData(bytes)),texture.TexelFormat,texture.Width,texture.Height});return id;
        }
        var mats=new Dictionary<int,MaterialBuilder>();var materialReport=new List<object>();
        for(int i=0;i<high.Materials.Length;i++)
        {
            if(!selected.Contains(i))continue;
            var mat=high.Materials[i];string template=interior?(mat.Name=="Glass_Clear"?"Sticker 01":"Matt"):mat.Name switch
            {
                "Carpaint" or "Carbon" or "Carbon_2"=>"Matt",
                "Tyre"=>"Tyre Tread","Rim" or "Rim_Spokes" or "Rim_Decals"=>"Wheel",
                "Brake_Disk"=>"Discs","Glass_Clear"=>"Window Netting",
                // Keep exterior controls on the donor cabin render path.
                // The body shader causes visible interference with the HR wheel.
                "INT_Cockpit_LR"=>"Cabin",
                "Metal" or "Allu" or "Exhaust" or "Mechanicals"=>"Suspension ",_=>"Matt"
            };
            var shader=(PssgShaderInstance)shaders[template].DeepClone();shaderLib.AppendChild(shader);shader.Id=(interior?"f04_int_material_":"f04_ext_material_")+i;
            var group=shader.GetShaderGroup();var xml=new XElement("root");group.WriteXml(xml);var definitions=xml.Elements().Single().Elements("SHADERINPUTDEFINITION").ToArray();
            var diffuse=Texture(mat.Textures["txDiffuse"],mat);
            // Bake source detail colour/mask into the native diffuse atlas.
            // Normal channels are repacked; packed specular, damage and emission remain approximate.
            var bindings=new List<(string Name,string Texture)>{("TDiffuseAlphaMap",diffuse)};
            if(mat.Textures.TryGetValue("txNormal",out var normal) && definitions.Any(d=>(string?)d.Attribute("name")=="TNormalMap"))bindings.Add(("TNormalMap",Texture(normal,normalMap:true)));
            foreach(var (name,textureName) in bindings)
            {
                int index=Array.FindIndex(definitions,d=>(string?)d.Attribute("name")==name);if(index<0)throw new InvalidDataException("Missing native diffuse parameter");
                foreach(var prior in shader.Inputs.Where(s=>s.ParameterId==index).ToArray())shader.RemoveChild(prior);
                var input=new PssgShaderInput(pssg,shader){ParameterId=(uint)index,Type="texture",Texture="#"+textureName};shader.AppendChild(input);
            }
            void Constant(string name,float value)
            {
                int index=Array.FindIndex(definitions,d=>(string?)d.Attribute("name")==name);if(index<0)return;
                foreach(var prior in shader.Inputs.Where(s=>s.ParameterId==index).ToArray())shader.RemoveChild(prior);
                var input=new PssgShaderInput(pssg,shader){ParameterId=(uint)index,Type="constant",Format="float"};
                input.DisplayValue=value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);shader.AppendChild(input);
            }
            // Source AO is already in the colour atlas. A donor UV occlusion map
            // would darken unrelated Ferrari surfaces; detail normals stay pending.
            Constant("OcclusionMapScale",0);Constant("NormalMapScale",interior && !mat.Textures.ContainsKey("txNormal")?0:1);
            if(interior && mat.Name=="Glass_Clear")Constant("EmissiveMapScale",0);
            // The AC multi-map reflection/specular mask has no native mapping.
            // Keep these body surfaces on the donor diffuse shader until it does.
            if(mat.Name is "Carpaint" or "Carbon" or "Carbon_2")
            {
                Constant("FresnelMin",0);Constant("FresnelMax",0);Constant("FresnelPower",1);
                Constant("MetallicSpecularScale",0);Constant("SpecularHighlightStrength",.35f);
            }
            else
            {
                if(mat.Properties.TryGetValue("fresnelC",out var fresnel))Constant("FresnelMin",fresnel[0]);
                if(mat.Properties.TryGetValue("fresnelMaxLevel",out var maxFresnel))Constant("FresnelMax",maxFresnel[0]);
                if(mat.Properties.TryGetValue("fresnelEXP",out var power))Constant("FresnelPower",power[0]);
            }
            shader.ParameterSavedCount=(uint)shader.Inputs.Count();
            mats[i]=new MaterialBuilder(shader.Id).WithMetallicRoughnessShader().WithBaseColor(Vector4.One);
            materialReport.Add(new{Source=mat.Name,mat.Shader,NativeTemplate=template,NativeGroup=shader.ShaderGroup,NativeInstance=shader.Id,Diffuse=diffuse,Unsupported="Source normals repacked; specular/damage/emission approximate; detail colour baked"});
        }
        return new(mats,materialReport,textureReport);
    }
}
