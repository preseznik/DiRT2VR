using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Keep terrain's layer controls and vertex streams; object_simple cannot express
// world-mapped infield detail, rock/soil blends or the baked terrain lightmaps.
internal sealed class MizuTerrain
{
    readonly Dictionary<string,(XElement Group,ShaderInputInfo Layout)> templates=new(StringComparer.Ordinal);
    readonly List<object> mappings=[];
    static string Target(string source)=>source switch
    {
        "terrain_rockbank_flat.fx"=>"terrain_road_blend_flat.fx",
        "terrain_rockbank.fx"=>"terrain_road_blend.fx",
        "terrain_infield_overlay_feather.fx"=>"terrain_infield.fx",
        "terrain_road_flat.fx" or "terrain_road_blend_flat_simple.fx"=>"terrain_road_blend_flat.fx",
        "terrain_simple_distant.fx"=>"terrain_simple.fx",
        "terrain_simple_nm_distant.fx"=>"terrain_simple_nm.fx",
        "decal_ao_vc_flat.fx"=>"decal_ao.fx",
        _=>source
    };
    internal MizuTerrain(string game,IEnumerable<string> sourceGroups,Dictionary<string,string> inputs)
    {
        var needed=sourceGroups.Select(Target).ToHashSet(StringComparer.Ordinal);
        if(needed.Contains("decal_ao.fx"))
        {
            string path=Files.Inside(game,"tracks/croatia/croatia_raid/tracksplit.pssg");var file=Files.Pssg(path);
            var layout=ShaderInputInfo.CreateFromPssg(file).Single(l=>l.ShaderGroupId=="decal_ao.fx");
            var root=new XElement("root");file.GetObject<PssgShaderGroup>("decal_ao.fx".AsMemory()).WriteXml(root);
            templates.Add("decal_ao.fx",(root.Elements().Single(),layout));inputs.TryAdd(path,Files.Hash(path));
        }
        foreach(var relative in new[]{"tracks/baja/baja_iron/route_0/routesplit.pssg", "tracks/baja/baja_rally/route_0/routesplit.pssg",
            "tracks/japan/shibuya/route_0/routesplit.pssg", "tracks/london/battersea/route_0/routesplit.pssg", "tracks/usa/la_stadium/route_0/routesplit.pssg"})
        {
            string path=Files.Inside(game,relative);
            var file=Files.Pssg(path);bool used=false;
            foreach(var layout in ShaderInputInfo.CreateFromPssg(file))
            {
                if(!needed.Contains(layout.ShaderGroupId)||templates.ContainsKey(layout.ShaderGroupId))continue;
                var root=new XElement("root");file.GetObject<PssgShaderGroup>(layout.ShaderGroupId.AsMemory()).WriteXml(root);
                templates.Add(layout.ShaderGroupId,(root.Elements().Single(),layout));used=true;
            }
            if(used)inputs.TryAdd(path,Files.Hash(path));
            if(needed.All(templates.ContainsKey))break;
        }
        if(!needed.All(templates.ContainsKey))throw new InvalidDataException("Missing stock terrain shaders: "+string.Join(",",needed.Where(n=>!templates.ContainsKey(n))));
    }
    internal NativeMaterial Convert(PssgShaderInstance shader,Vector3 origin,Dictionary<string,string> textures,Func<string,string,string> resolve)
    {
        string source=shader.GetShaderGroup().Id,target=Target(source);var template=templates[target];
        var root=new XElement("root");shader.GetShaderGroup().WriteXml(root);shader.WriteXml(root);
        var sourceDefinitions=root.Element("SHADERGROUP")!.Elements("SHADERINPUTDEFINITION").ToArray();
        var targetDefinitions=template.Group.Elements("SHADERINPUTDEFINITION").ToArray();
        var instance=root.Element("SHADERINSTANCE")!;var removed=new List<string>();
        foreach(var input in instance.Elements("SHADERINPUT").ToArray())
        {
            var definition=sourceDefinitions[(int)input.Attribute("parameterID")!];string name=(string)definition.Attribute("name")!;
            int index=Array.FindIndex(targetDefinitions,d=>(string?)d.Attribute("name")==name);
            if(index<0){removed.Add(name);input.Remove();continue;}
            if((string?)definition.Attribute("type")!=(string?)targetDefinitions[index].Attribute("type") ||
                (string?)definition.Attribute("format")!=(string?)targetDefinitions[index].Attribute("format"))throw new InvalidDataException("Terrain parameter format mismatch: "+source+"/"+name);
            input.SetAttributeValue("parameterID",index);
            if(input.Attribute("texture") is {} texture)textures[name]=resolve(name,texture.Value);
            // Infield maps 2+ and their mask sample world XZ. Preserve the original
            // phase after translating the venue into the DiRT 2 route coordinate frame.
            if(target.StartsWith("terrain_infield",StringComparison.Ordinal) && (name is "Map2UVScaleAndOffset" or "Map3UVScaleAndOffset" or "Map4UVScaleAndOffset"))
            {
                var f=input.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                if(f.Length!=4)throw new InvalidDataException("Expected terrain mapping float4");
                if(f[0]!=0)f[2]+=origin.X/f[0];if(f[1]!=0)f[3]+=origin.Z/f[1];
                input.Value=string.Join(" ",f.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
            }
        }
        bool unusedThirdLayer=source is "terrain_rockbank_flat.fx" or "terrain_rockbank.fx" or "terrain_road_flat.fx" or "terrain_road_blend_flat_simple.fx";
        if(unusedThirdLayer)
        {
            // GRID 2 rockbank: lerp(map1(uv0), map2(uv0), colour.r).
            // DiRT 2 road blend has those UV roles and uses colour.b for the
            // first blend; colour.g enables its optional third layer. Two-layer
            // road aliases also need explicit textures for that unused layer.
            foreach(string name in new[]{"TDiffuseSpecMap3","TBlendMap3","TNormalMap3"})
            {
                int index=Array.FindIndex(targetDefinitions,d=>(string?)d.Attribute("name")==name);
                if(index<0)continue;
                string primary=name=="TNormalMap3"?"TNormalMap1":"TDiffuseSpecMap1";
                string fallback=name=="TBlendMap3"?"nord_black":textures.GetValueOrDefault(primary)??"nord_normal";
                textures[name]=fallback;
                var existing=instance.Elements("SHADERINPUT").SingleOrDefault(i=>(int)i.Attribute("parameterID")! == index);
                if(existing is null)instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",index),new XAttribute("type","texture"),new XAttribute("texture",name)));
            }
        }
        if(source=="decal_ao_vc_flat.fx")
        {
            int index=Array.FindIndex(targetDefinitions,d=>(string?)d.Attribute("name")=="TNormalMap");
            if(index<0)throw new InvalidDataException("Native decal shader lacks normal input");
            textures["TNormalMap"]="nord_normal_dxt5";
            instance.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",index),new XAttribute("type","texture"),new XAttribute("texture","nord_normal_dxt5")));
        }
        instance.SetAttributeValue("shaderGroup","#"+target);instance.SetAttributeValue("parameterCount",targetDefinitions.Length);
        instance.SetAttributeValue("parameterSavedCount",instance.Elements("SHADERINPUT").Count());
        mappings.Add(new{Shader=shader.Id,Source=source,Target=target,RemovedInputs=removed,TextureInputs=textures.Keys.Where(n=>n!="txDiffuse").ToArray(),
            VertexColourMapping=source is "terrain_rockbank_flat.fx" or "terrain_rockbank.fx"?"Source red blend moved to target blue; target third-layer green disabled":unusedThirdLayer?"Target third-layer green disabled; existing first-layer blend retained":"Source channels retained",AllSourceUVSetsPreserved=true,
            SourceShadowAlphaToTargetRedGreen=source=="decal_ao_vc_flat.fx",SourceIndirectLuminanceToTargetBlue=source=="decal_ao_vc_flat.fx",ExplicitUnusedRoadLayer=unusedThirdLayer});
        return new(template.Group,new XElement(instance),template.Layout,source!="decal_ao_vc_flat.fx");
    }
    internal static Vector4 Colour(string shader,Vector4 colour)=>shader is "terrain_rockbank_flat.fx" or "terrain_rockbank.fx"?new(colour.X,0,colour.X,colour.W):shader is "terrain_road_flat.fx" or "terrain_road_blend_flat_simple.fx"?new(colour.X,0,colour.Z,colour.W):colour;
    internal void Write(string output)=>Files.Json(Path.Combine(output,"terrain-materials.json"),mappings);
}
