using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;

internal static class PikesCanopies
{
    sealed record Leaf(Vector3 Normal,Vector4 Colour,float Height);
    static (Vector3,Vector2) Key(Vector3 p,Vector2 uv)=>(new(MathF.Round(p.X*1000),MathF.Round(p.Y*1000),MathF.Round(p.Z*1000)),new((float)(Half)uv.X,(float)(Half)uv.Y));
    internal static void Apply(Scene target,string rally,PikesSourceProfile profile,string output)
    {
        var contracts=XDocument.Parse(PikesResources.Text("canopies.xml"));
        var groups=contracts.Root!.Elements("SHADERGROUP").ToDictionary(g=>(string)g.Attribute("id")!);
        var defaults=contracts.Root.Elements("SHADERINSTANCE").ToDictionary(g=>((string)g.Attribute("shaderGroup")!)[1..]);
        var light=profile.Lighting(rally);
        float N(string name)=>float.Parse(PikesSourceProfile.Parameter(light,name),CultureInfo.InvariantCulture);
        uint tint=unchecked((uint)int.Parse(PikesSourceProfile.Parameter(light,"treeAmbientTintColour"),CultureInfo.InvariantCulture));
        var sourceGroups=PikesSponsorBarrier.Read(profile.PathIn(rally,"trees.pssg")).Elements<EgoEngineLibrary.Graphics.Pssg.Elements.PssgShaderInstance>().ToDictionary(m=>m.Id,m=>m.GetShaderGroup().Id);
        var sources=new Dictionary<string,Dictionary<(Vector3,Vector2),Leaf>>();var families=new Dictionary<string,string>();
        foreach(bool rigid in new[]{false,true})
        {
            string path=Path.Combine(output,rigid?"source-rigid-leaves":"source-facing-leaves");Directory.CreateDirectory(path);
            var source=PikesScene.ReadFoliage(rally,path,profile,true,rigid);
            foreach(var input in source.Inputs)target.Inputs.TryAdd(input.Key,input.Value);
            foreach(var mesh in source.Visuals)
            {
                string hash=source.Textures[mesh.Material.Textures["txDiffuse"]].Hash;
                string family=rigid?"pikes_canopy_rigid.fx":mesh.Material.Shader=="treesheet_foliage_norm.fx"?"pikes_canopy_sheet.fx":"pikes_canopy_facing.fx";
                // Material.Shader stores the source family on this read path.
                if(!rigid&&mesh.Material.Shader is not ("treesheet_foliage_norm.fx" or "facing_foliage.fx"))
                    family=SourceFamily(mesh.Material.Name);
                if(families.TryGetValue(hash,out var prior)&&prior!=family)throw new InvalidDataException("Ambiguous leaf atlas family");
                families[hash]=family;if(!sources.ContainsKey(hash))sources.Add(hash,[]);
                for(int i=0;i<mesh.Positions.Length;i++)
                {
                    var key=Key(mesh.Positions[i],rigid?mesh.UV[i]:mesh.TexCoords![0][i]);var leaf=new Leaf(mesh.Normals[i],mesh.Colors![i],mesh.TexCoords![^1][i].X);
                    if(sources[hash].TryGetValue(key,out var previous)&&(Vector4.Distance(previous.Colour,leaf.Colour)>.01f||Vector3.Dot(previous.Normal,leaf.Normal)<.99f))throw new InvalidDataException("Ambiguous source canopy vertex");
                    sources[hash][key]=leaf;
                }
            }
        }
        var prototype=target.Visuals.First(m=>m.Material.Native?.Group.Attribute("id")?.Value=="pikes_foliage.fx").Material;
        var materials=new Dictionary<string,Material>();var seen=new HashSet<string>();var counts=new Dictionary<string,int>();
        for(int m=0;m<target.Visuals.Count;m++)
        {
            var mesh=target.Visuals[m];
            if(!mesh.Material.Textures.TryGetValue("txDiffuse",out var key)||!target.Textures.TryGetValue(key,out var diffuse)||!sources.TryGetValue(diffuse.Hash,out var leaves))continue;
            string family=families[diffuse.Hash];bool rigid=family=="pikes_canopy_rigid.fx",sheet=family=="pikes_canopy_sheet.fx";
            if(!rigid&&mesh.Material.Native?.Group.Attribute("id")?.Value!="pikes_foliage.fx")continue;
            if(!materials.TryGetValue(mesh.Material.Id,out var material))
            {
                var basis=mesh.Material.Native is null?prototype:mesh.Material;
                var group=new XElement(groups[family]);var instance=new XElement(defaults[family]);
                var baseDefinitions=basis.Native!.Group.Elements("SHADERINPUTDEFINITION").ToArray();
                var baseInputs=basis.Native.Instance.Elements("SHADERINPUT").ToDictionary(i=>(string)baseDefinitions[(int)i.Attribute("parameterID")!].Attribute("name")!);
                var textures=new Dictionary<string,string>(basis.Textures){["TDiffuseAlphaMap"]=key,["txDiffuse"]=key};
                var defs=group.Elements("SHADERINPUTDEFINITION").ToArray();
                float ambient=N("treeAmbientMultiplier")*N("gi_indirectLightingIntensity")*(sheet?N("treeSheetAmbient"):1);
                var constants=new Dictionary<string,float[]>{
                    ["TreeDiffuseScale"]=[N(sheet?"treeSheetDiffuse":"treeLightResponse")*N("gi_directLightingIntensity")],
                    ["TreeAmbientColour"]=[((tint>>16)&255)/255f*ambient,((tint>>8)&255)/255f*ambient,(tint&255)/255f*ambient],
                    ["TreeShadowing"]=[N("treeShadowing")],["TreeShapeScale"]=[N("treeDarkenCenter")],
                    ["TreeColourBalance"]=[N("treeDarkest")],["TreeLightWrapping"]=[N("treeLightWrapping")]};
                foreach(var input in instance.Elements("SHADERINPUT").ToArray())
                {
                    int parameter=(int)input.Attribute("parameterID")!;string name=(string)defs[parameter].Attribute("name")!;
                    if((string?)input.Attribute("type")=="texture")
                    {
                        if(!textures.ContainsKey(name))throw new InvalidDataException("Missing source canopy texture: "+name);
                        input.SetAttributeValue("texture",name);input.Value="";
                    }
                    else if(constants.TryGetValue(name,out var values))input.Value=string.Join(" ",values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
                    else if(baseInputs.TryGetValue(name,out var original)) { var copy=new XElement(original);copy.SetAttributeValue("parameterID",parameter);input.ReplaceWith(copy); }
                    else throw new InvalidDataException("Missing source canopy constant: "+name);
                }
                instance.SetAttributeValue("id",mesh.Material.Name);instance.SetAttributeValue("shaderGroup","#"+family);
                uint stride=rigid?32u:40u;
                var fields=rigid?new[]{new ShaderVertexInputInfo("Vertex","float3",0,stride),new("ST","half2",12,stride),new("ST","half2",16,stride),new("Normal","float3",20,stride)}:
                    new[]{new ShaderVertexInputInfo("Vertex","float3",0,stride),new("Color","uint_color_argb",12,stride),new("ST","half2",16,stride),new("ST","half2",20,stride),new("ST","float4",24,stride)};
                var layout=new ShaderInputInfo(family,[new ShaderBlockInputInfo(fields.ToList())]);
                material=mesh.Material with{Textures=textures,Native=new(group,instance,layout,false),AlphaTest=true};materials.Add(mesh.Material.Id,material);
            }
            int length=mesh.Positions.Length;var normals=mesh.Normals.ToArray();var colours=new Vector4[length];var coords=Enumerable.Range(0,rigid?2:4).Select(_=>new Vector2[length]).ToArray();
            for(int v=0;v<length;v++)
            {
                if(!leaves.TryGetValue(Key(mesh.Positions[v],rigid?mesh.UV[v]:mesh.TexCoords![0][v]),out var leaf))throw new InvalidDataException("Unmatched source leaf: "+mesh.Name+" "+mesh.Positions[v]+" UV "+mesh.UV[v]+" raw "+mesh.TexCoords?[0][v]+" nearest "+string.Join("; ",leaves.Keys.OrderBy(k=>Vector3.DistanceSquared(k.Item1,Key(mesh.Positions[v],Vector2.Zero).Item1)).Take(3)));
                coords[0][v]=rigid?mesh.UV[v]:mesh.TexCoords![0][v];
                if(rigid)
                {
                    if(Math.Abs(leaf.Colour.X-leaf.Colour.Y)>.001f||Math.Abs(leaf.Colour.X-leaf.Colour.Z)>.001f)throw new InvalidDataException("Rigid leaf shading is not grayscale");
                    coords[1][v]=new(leaf.Colour.X,leaf.Height);
                    // Preserve the accepted eight-bit radial normal encoding.
                    normals[v]=new(Quantize(leaf.Normal.X),Quantize(leaf.Normal.Y),Quantize(leaf.Normal.Z));
                }
                else
                {
                    coords[1][v]=mesh.TexCoords![1][v];coords[2][v]=new(leaf.Colour.X,leaf.Colour.Y);coords[3][v]=new(leaf.Colour.Z,leaf.Height);
                    var c=sheet?leaf.Colour:new Vector4(leaf.Normal*.5f+new Vector3(.5f),leaf.Colour.W);
                    // The library writes Z,Y,X,W; native ARGB expects W,X,Y,Z.
                    colours[v]=new(c.Y,c.X,c.W,c.Z);
                }
            }
            target.Visuals[m]=mesh with{Material=material,Normals=normals,Colors=colours,TexCoords=coords};seen.Add(diffuse.Hash);counts[family]=counts.GetValueOrDefault(family)+length;
        }
        if(!seen.SetEquals(sources.Keys))throw new InvalidDataException("A source canopy family was omitted");
        Files.Json(Path.Combine(output,"canopy-conversion.json"),new{SourceAtlases=seen.Count,Materials=materials.Count,Vertices=counts,SourceNormals=true,GeometryAndIndicesUnchanged=true});
        string SourceFamily(string name)
        {
            return sourceGroups[name] switch{
                "treesheet_foliage_norm.fx"=>"pikes_canopy_sheet.fx","facing_foliage.fx"=>"pikes_canopy_facing.fx",_=>throw new InvalidDataException("Unknown source leaf family")};
        }
    }
    static float Quantize(float v)=>Math.Clamp(MathF.Round((v*.5f+.5f)*255),0,255)/255f*2-1;
}
