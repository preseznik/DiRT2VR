using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// The two tunnel fixtures share an atlas. Keep the existing geometry/layout,
// restore separate lamp emission and shade the housing with bounded ambient AO.
internal static class MizuFixtures
{
    internal static void Write(string grid2,string dirt2,string candidate)
    {
        string temporary=Path.Combine(candidate,"fixture-assembly");Directory.CreateDirectory(temporary);
        Apply(grid2,dirt2,candidate,temporary);
        File.Move(Path.Combine(temporary,"routesplit.pssg"),Path.Combine(candidate,"routesplit.pssg"),true);
        File.Move(Path.Combine(temporary,"tracksplit.pssg"),Path.Combine(candidate,"shared/tracksplit.pssg"),true);
        File.Move(Path.Combine(temporary,"fixture-materials.json"),Path.Combine(candidate,"fixture-materials.json"));Directory.Delete(temporary);
        string materialPath=Path.Combine(candidate,"materials.json");var materials=JsonNode.Parse(File.ReadAllText(materialPath))!.AsArray();
        foreach(var material in materials.Where(m=>m!["Name"]!.GetValue<string>() is "core_light_flood_day_01!0" or "core_light_flood_day_01!1"))
        {
            material!["Target"]="object_simple_emissive.fx";
            material["Approximation"]="Source tunnel fixture specular, occlusion and emission maps restored; native radiance scaled for tunnel shading";
        }
        File.WriteAllText(materialPath,materials.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        string adapterPath=Path.Combine(candidate,"source-adapter.json");var adapter=JsonNode.Parse(File.ReadAllText(adapterPath))!;adapter["NativeTunnelFixtures"]=true;
        File.WriteAllText(adapterPath,adapter.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void Apply(string grid2,string dirt2,string candidate,string output)
    {
        string venue=Files.Inside(grid2,"tracks/locations/p2p/okutama");
        string objectsPath=Files.Inside(venue,"objects.pssg"),texturesPath=Files.Inside(venue,"patchup_ot.pssg"),donorPath=Files.Inside(dirt2,"tracks/london/battersea/objects.pssg");
        var source=Files.Pssg(objectsPath);var sourceTextures=Files.Pssg(texturesPath);var donor=Files.Pssg(donorPath);
        var route=Files.Pssg(Files.Inside(candidate,"routesplit.pssg"));var resources=Files.Pssg(Files.Inside(candidate,"shared/tracksplit.pssg"));
        var targetGroup=donor.GetObject<PssgShaderGroup>("object_simple_emissive.fx".AsMemory());
        var groupLibrary=route.Elements<PssgLibrary>().Single(l=>l.Type=="SHADERGROUP");
        var groupXml=new XElement("root");targetGroup.WriteXml(groupXml);
        if(!route.Elements<PssgShaderGroup>().Any(g=>g.Id==targetGroup.Id))groupLibrary.AppendChild(PssgElement.ReadXml(groupXml.Elements().Single(),route,groupLibrary));
        var textureLibrary=resources.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");
        foreach(var previous in resources.Elements<PssgTexture>().Where(t=>t.Id.StartsWith("mizu_fixture_",StringComparison.Ordinal)).ToArray())textureLibrary.RemoveChild(previous);
        var definitions=targetGroup.InputDefinitions.ToArray();var added=new Dictionary<string,string>();var edits=new List<object>();
        using var materials=JsonDocument.Parse(File.ReadAllText(Files.Inside(candidate,"materials.json")));
        var rows=materials.RootElement.EnumerateArray().ToArray();
        var beforeGeometry=GeometryHashes(route);
        if(beforeGeometry.Length==0)throw new InvalidDataException("Fixture audit found no geometry buffers");
        var layouts=EgoEngineLibrary.Formats.Pssg.ShaderInputInfo.CreateFromPssg(donor).Where(l=>l.ShaderGroupId is "object_simple.fx" or "object_simple_emissive.fx").ToArray();
        if(layouts.Length!=2||!layouts[0].BlockInputs.SelectMany(b=>b.VertexInputs).Select(v=>v.ToString()).SequenceEqual(layouts[1].BlockInputs.SelectMany(b=>b.VertexInputs).Select(v=>v.ToString())))
            throw new InvalidDataException("Fixture vertex layouts differ; rebuild geometry instead");
        for(int i=0;i<rows.Length;i++)
        {
            string name=rows[i].GetProperty("Name").GetString()!;
            if(name is not ("core_light_flood_day_01!0" or "core_light_flood_day_01!1"))continue;
            if(rows[i].GetProperty("Target").GetString() is not ("object_simple.fx" or "object_simple_emissive.fx"))throw new InvalidDataException("Unexpected fixture shader");
            var original=source.GetObject<PssgShaderInstance>(name.AsMemory());var sourceDefinitions=original.GetShaderGroup().InputDefinitions.ToArray();
            var inputs=original.Inputs.ToDictionary(p=>sourceDefinitions[p.ParameterId].InputName,StringComparer.Ordinal);
            var shader=route.GetObject<PssgShaderInstance>(("nord_material_"+i).AsMemory());
            var oldDefinitions=shader.GetShaderGroup().InputDefinitions.ToArray();
            string diffuse=shader.Inputs.Single(p=>oldDefinitions[p.ParameterId].InputName=="TDiffuseAlphaMap").Texture;
            shader.RemoveChildElements();shader.ShaderGroup="#object_simple_emissive.fx";shader.ParameterCount=(ushort)definitions.Length;
            float emission=name.EndsWith("!1",StringComparison.Ordinal)?.10f:.025f;
            foreach(string inputName in new[]{"TDiffuseAlphaMap","TSpecularMap","TOcclusionMap","TEmissiveMap"})
            {
                int index=Array.FindIndex(definitions,d=>d.InputName==inputName);
                if(index<0)throw new InvalidDataException("Missing fixture target input: "+inputName);
                string texture=diffuse;
                if(inputName!="TDiffuseAlphaMap")
                {
                    string reference=inputs[inputName].Texture;string id=reference[(reference.IndexOf('#')+1)..];
                    float scale=inputName=="TOcclusionMap"?.25f:inputName=="TEmissiveMap"?emission:.25f;
                    string key=id+"/"+scale.ToString("R",CultureInfo.InvariantCulture);
                    if(!added.TryGetValue(key,out texture!))
                    {
                        var sourceTexture=sourceTextures.GetObject<PssgTexture>(id.AsMemory());
                        var xml=new XElement("root");sourceTexture.WriteXml(xml);
                        var clone=(PssgTexture)PssgElement.ReadXml(xml.Elements().Single(),resources,textureLibrary);
                        clone.Id="mizu_fixture_"+added.Count;var dds=sourceTexture.ToDdsFile();ScaleRgb(dds,scale);dds.ToPssgElement(clone);
                        textureLibrary.AppendChild(clone);texture="tracksplit.pssg#"+clone.Id;added.Add(key,texture);
                    }
                }
                var input=new PssgShaderInput(route,shader){ParameterId=(uint)index,Type="texture",Texture=texture};shader.AppendChild(input);
            }
            shader.ParameterSavedCount=(uint)shader.Inputs.Count();edits.Add(new{Source=name,Target=shader.Id,SourceEmission=inputs["EmissiveDayScale"].DisplayValue,NativeEmissionScale=emission,OcclusionScale=.25f,SpecularScale=.25f});
        }
        if(edits.Count!=2)throw new InvalidDataException("Expected both tunnel fixture materials");
        if(!beforeGeometry.SequenceEqual(GeometryHashes(route)))throw new InvalidDataException("Fixture edit changed geometry");
        Files.Save(route,Path.Combine(output,"routesplit.pssg"));Files.Save(resources,Path.Combine(output,"tracksplit.pssg"));
        Files.Json(Path.Combine(output,"fixture-materials.json"),new{Edits=edits,AddedTextures=added,GeometryBuffersByteIdentical=true,HdrUnchanged=true,
            Approximation="Source diffuse/specular/emissive maps retained; tunnel-only ambient/specular and emission are scaled for the native DiRT 2 shader. Original GRID 2 GI is not reconstructed",RuntimeValidated=false,
            Inputs=new[]{objectsPath,texturesPath,donorPath}.ToDictionary(p=>p,Files.Hash)});
    }
    static string[] GeometryHashes(PssgFile file)=>file.Elements<PssgElement>().Where(e=>e.Name is "DATABLOCKDATA" or "INDEXSOURCEDATA")
        .Select(e=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(e.Value))).ToArray();
    static void ScaleRgb(EgoEngineLibrary.Graphics.Dds.DdsFile file,float scale)
    {
        if(file.header.ddspf.fourCC!=0x31545844||file.bdata.Length%8!=0)throw new InvalidDataException("Expected DXT1 fixture atlas");
        for(int offset=0;offset<file.bdata.Length;offset+=8)for(int endpoint=0;endpoint<2;endpoint++)
        {
            var colour=file.bdata.AsSpan(offset+endpoint*2,2);ushort c=BinaryPrimitives.ReadUInt16LittleEndian(colour);
            int r=(int)MathF.Round((c>>11)*scale),g=(int)MathF.Round(((c>>5)&63)*scale),b=(int)MathF.Round((c&31)*scale);
            BinaryPrimitives.WriteUInt16LittleEndian(colour,(ushort)((r<<11)|(g<<5)|b));
        }
    }
}
