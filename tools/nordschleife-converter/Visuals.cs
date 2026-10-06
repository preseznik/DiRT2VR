using System.Numerics;
using System.Xml.Linq;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

namespace DiRT2VR.Nordschleife;

internal static class Visuals
{
    sealed class State : PssgModelWriterState { }
    static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "ksPerPixel", "ksTree", "ksGrass", "ksPerPixelMultiMap", "ksPerPixelSimpleRefl", "ksPerPixelReflection",
        "ksPerPixelNM_UVMult", "ksPerPixelNM", "ksPerPixelMultiMap_NMDetail", "ksMultilayer_objsp",
        "ksMultilayer_fresnel_nm", "ksPerPixelMultiMap_AT", "ksPerPixelAT", "ksPerPixelAlpha",
        "ksPerPixelMultiMap_AT_NMDetail", "ksPerPixelMultiMapSimpleRefl", "ksFlags"
    };
    internal static string Target(Material material)
    {
        if (!Supported.Contains(material.Shader)) throw new InvalidDataException("Unmapped AC shader: " + material.Shader);
        if (material.Blend != 0 || material.Shader == "ksPerPixelAlpha") return "glass_simple.fx";
        if (RoadMaterial.Read(material) is not null) return "terrain_infield.fx";
        // AC foliage is already positioned geometry. EGO's treesheet shader requires
        // TreeManager instance streams and billboard offsets that terrain does not supply.
        // The static object shader clips diffuse alpha and preserves source positions.
        return "object_simple.fx";
    }
    internal static bool IsOpaque(Material material) => Target(material) is "object_simple.fx" or "terrain_infield.fx" &&
        !material.AlphaTest && material.Shader is not ("ksTree" or "ksGrass" or "ksPerPixelAT") &&
        !material.Shader.Contains("_AT",StringComparison.Ordinal);
    static XElement Element(PssgElement element)
    {
        var root = new XElement("root"); element.WriteXml(root); return root.Elements().Single();
    }
    internal static (Bounds[] Before, Bounds[] After) Build(Scene scene, string donor, string output,bool independentTiles=false)
    {
        var route = Files.Pssg(Path.Combine(donor,"route_1/routesplit.pssg"));
        var roots = route.Elements<PssgNode>().Where(n => n.Id.StartsWith("ROOT_",StringComparison.Ordinal)).Reverse().ToArray();
        if (roots.Length != 25) throw new InvalidDataException("The lab expects the inspected 25-tile Battersea donor.");
        Bounds TileBounds(PssgNode root) => root.ChildElements.OfType<PssgRenderNode>()
            .Select(n => new Bounds(n.BoundingBox.BoundsMin,n.BoundingBox.BoundsMax)).Aggregate((a,b)=>a.Union(b));
        var before=roots.Select(TileBounds).ToArray();
        var (parts,tileSize)=Tiles.Partition(scene.Visuals,independentTiles?null:roots.Length);
        var depthLayout=ShaderInputInfo.CreateFromPssg(route).Single(s=>s.ShaderGroupId=="batched_track.fx");
        var depthTemplate=Element(route.GetObject<PssgShaderGroup>("batched_track.fx".AsMemory()));
        var depthMaterial=Element(route.GetObject<PssgShaderInstance>("batchmaterial".AsMemory()));
        var objects=Files.Pssg(Path.Combine(donor,"objects.pssg"));
        var trees=Files.Pssg(Path.Combine(donor,"trees.pssg"));
        var textureSource=Files.Pssg(Path.Combine(donor,"tracksplit.pssg"));
        var layouts=new Dictionary<string,ShaderInputInfo>();
        var templates=new Dictionary<string,(XElement Group,XElement Instance)>();
        foreach(var file in new[]{objects,trees,route})
        foreach(var layout in ShaderInputInfo.CreateFromPssg(file))
        {
            layouts[layout.ShaderGroupId]=layout;
            var group=file.GetObject<PssgShaderGroup>(layout.ShaderGroupId.AsMemory());
            var instance=file.Elements<PssgShaderInstance>().First(i=>i.ShaderGroup=="#"+group.Id);
            templates[group.Id]=(Element(group),Element(instance));
        }
        var textureTemplate=Element(textureSource.Elements<PssgTexture>().First());
        var resources=Files.Document(textureSource);
        var textureLibrary=resources.Descendants("LIBRARY").Single(e=>(string?)e.Attribute("type")=="RENDERINTERFACEBOUND");
        // Strip geometry in memory before XML cloning, avoiding a huge text copy of the donor buffers.
        foreach(var instance in route.Elements<PssgRenderInstance>().ToArray())instance.ParentElement!.RemoveChild(instance);
        foreach(var lib in route.Elements<PssgLibrary>().Where(l=>l.Type!="NODE").ToArray())lib.RemoveChildElements();
        var doc=Files.Document(route);
        var renderTemplate=doc.Descendants("RENDERNODE").First();
        foreach(var root in doc.Descendants("NODE").Where(n=>((string?)n.Attribute("id"))?.StartsWith("ROOT_",StringComparison.Ordinal)==true))
        foreach(string prefix in new[]{"HIGH_","LOW_","HIGHBATCH_","LOWBATCH_"})
        {
            string id=prefix+((string)root.Attribute("id")!)[5..];
            if(root.Elements("RENDERNODE").Any(n=>(string?)n.Attribute("id")==id))continue;
            var node=new XElement(renderTemplate);node.SetAttributeValue("id",id);node.SetAttributeValue("nickname",id);root.Add(node);
        }
        if(independentTiles)
        {
            var oldRoots=doc.Descendants("NODE").Where(n=>((string?)n.Attribute("id"))?.StartsWith("ROOT_",StringComparison.Ordinal)==true).ToArray();
            if(oldRoots.Select(r=>r.Parent).Distinct().Count()!=1)throw new InvalidDataException("Terrain roots have different parents.");
            var parent=oldRoots[0].Parent!;var template=new XElement(oldRoots[0]);
            foreach(var root in oldRoots)root.Remove();
            // Runtime terrain registration visits siblings in reverse file order.
            for(int i=parts.Count-1;i>=0;i--)
            {
                var root=new XElement(template);root.SetAttributeValue("id","ROOT_"+i);root.SetAttributeValue("nickname","ROOT_"+i);
                foreach(var node in root.Elements("RENDERNODE"))
                {
                    string prefix=((string)node.Attribute("id")!).Split('_')[0];
                    node.SetAttributeValue("id",prefix+"_"+i);node.SetAttributeValue("nickname",prefix+"_"+i);
                }
                parent.Add(root);
            }
        }
        XElement Library(string type)=>doc.Descendants("LIBRARY").Single(e=>(string?)e.Attribute("type")==type);
        Library("SHADERGROUP").Add(depthTemplate);
        Library("SHADERINSTANCE").Add(depthMaterial);
        var sourceMaterials=scene.Visuals.Select(m=>m.Material).DistinctBy(m=>m.Id).OrderBy(m=>m.Id,StringComparer.Ordinal).ToArray();
        var ids=new Dictionary<string,string>();var shaderMappings=new List<object>();
        var addedGroups=new HashSet<string>();var textureBytes=new Dictionary<string,byte[]>();
        string TextureId(string name,bool opaque,bool roadShading=false)
        {
            if(!scene.Textures.TryGetValue(name,out var texture))throw new InvalidDataException("Missing embedded texture: "+name);
            string id="nord_tex_"+texture.Hash[..20]+(roadShading?"_road_shading":opaque?"_opaque":"");textureBytes.TryAdd(id,texture.Dds);return id;
        }
        for(int i=0;i<sourceMaterials.Length;i++)
        {
            var material=sourceMaterials[i];string target=Target(material);var template=templates[target];
            if(addedGroups.Add(target))Library("SHADERGROUP").Add(new XElement(template.Group));
            string id="nord_material_"+i;ids[material.Id]=id;
            var instance=new XElement(template.Instance);instance.SetAttributeValue("id",id);
            var definitions=template.Group.Elements("SHADERINPUTDEFINITION").ToArray();
            if(!material.Textures.TryGetValue("txDiffuse",out var diffuse))throw new InvalidDataException("Material has no diffuse map: "+material.Id);
            var grass=GrassMaterial.Read(material);
            var road=RoadMaterial.Read(material);
            if(grass is not null&&target!="object_simple.fx")throw new InvalidDataException("Grass ground must use an opaque target: "+material.Id);
            for(int j=0;j<definitions.Length;j++)
            {
                if((string?)definitions[j].Attribute("type")!="texture")continue;
                string name=(string)definitions[j].Attribute("name")!;
                var input=instance.Elements("SHADERINPUT").SingleOrDefault(x=>(int?)x.Attribute("parameterID")==j);
                if(input is null){input=new XElement("SHADERINPUT",new XAttribute("parameterID",j));instance.Add(input);}
                string texture=road is not null ? name.StartsWith("TDiffuseSpecMap",StringComparison.Ordinal)?TextureId(road.Detail,true):name=="TAmbientOcclusion"?TextureId(road.Shading,false,true):"nord_black" :
                    name=="TDiffuseAlphaMap"?TextureId(grass?.Detail??diffuse,IsOpaque(material)):name=="TOcclusionMap"?(grass is null?"nord_white":TextureId(grass.Shading,false)):name=="TNormalMap"?"nord_normal":"nord_black";
                input.SetAttributeValue("type","texture");
                input.SetAttributeValue("texture","tracksplit.pssg#"+texture);input.RemoveNodes();
            }
            if(road is not null)
            {
                var map=road.Mapping(scene.Origin);
                string mapping=string.Join(" ",new[]{map.X,map.Y,map.Z,map.W}.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
                foreach(var (name,value) in new[]{("Map2UVScaleAndOffset",mapping),("Map3UVScaleAndOffset",mapping),("PhongScale","0"),("FresnelMin","0"),("FresnelMax","0")})
                {
                    int parameter=Array.FindIndex(definitions,d=>(string?)d.Attribute("name")==name);
                    if(parameter<0)throw new InvalidDataException("Missing asphalt shader parameter: "+name);
                    var input=instance.Elements("SHADERINPUT").SingleOrDefault(x=>(int?)x.Attribute("parameterID")==parameter);
                    if(input is null){input=new XElement("SHADERINPUT",new XAttribute("parameterID",parameter));instance.Add(input);}
                    input.SetAttributeValue("type","constant");input.SetAttributeValue("format",definitions[parameter].Attribute("format")!.Value);input.Value=value;
                }
            }
            instance.SetAttributeValue("parameterSavedCount",instance.Elements("SHADERINPUT").Count());Library("SHADERINSTANCE").Add(instance);
            var attributes=layouts[target].BlockInputs.SelectMany(b=>b.VertexInputs).Select(v=>v.Name).Distinct().ToArray();
            shaderMappings.Add(new { Source=material.Id,material.Name,material.Shader,Target=target,Diffuse=road?.Detail??grass?.Detail??diffuse,Grass=grass,Road=road,
                VertexAttributes=attributes,SourceNormalsUsed=attributes.Contains("Normal"),
                DiffuseAlpha=IsOpaque(material)?"Opaque coverage; source mask alpha discarded, RGB retained":"Source coverage retained",
                Approximation=road is not null?"Primary asphalt detail uses source world XZ scale and phase; base shading uses original UVs. Additional mask layers, coloured shading and normal packing remain approximate":grass is null?"Diffuse, source UVs and normals retained; static cutout meshes use diffuse alpha clipping. Multilayer blending, AC specular/normal packing and animated effects are not reproduced":
                    "Primary source grass detail uses original world XZ scale and phase; source diffuse shading uses original UVs as occlusion. Remaining soil/detail mask blends and normal/specular packing are not reproduced" });
        }
        foreach(var id in textureBytes.Keys.Concat(new[]{"nord_white","nord_black","nord_normal"}))
        {
            var texture=new XElement(textureTemplate);texture.SetAttributeValue("id",id);textureLibrary.Add(texture);
        }
        route=Files.FromDocument(doc);
        textureSource=Files.FromDocument(resources);
        foreach(var texture in textureSource.Elements<PssgTexture>().Where(t=>t.Id.StartsWith("nord_",StringComparison.Ordinal)))
        {
            DdsFile dds;
            if(textureBytes.TryGetValue(texture.Id,out var payload))
            {
                using var memory=new MemoryStream(payload,false);dds=new DdsFile(memory);
                if(dds.header.width==0 || dds.header.height==0 || dds.header.width>8192 || dds.header.height>8192)throw new InvalidDataException("Invalid DDS dimensions.");
                if(texture.Id.EndsWith("_road_shading",StringComparison.Ordinal))RoadMaterial.PackShading(dds);
                else DdsAlpha.Prepare(dds,texture.Id.EndsWith("_opaque",StringComparison.Ordinal));
            }
            else
            {
                dds=new DdsFile();dds.header.width=1;dds.header.height=1;dds.header.mipMapCount=1;
                dds.header.ddspf.flags=DdsPixelFormat.Flags.DDPF_RGB|DdsPixelFormat.Flags.DDPF_ALPHAPIXELS;
                dds.header.ddspf.rGBBitCount=32;dds.header.ddspf.rBitMask=0x00ff0000;dds.header.ddspf.gBitMask=0x0000ff00;
                dds.header.ddspf.bBitMask=0x000000ff;dds.header.ddspf.aBitMask=0xff000000;
                dds.bdata=texture.Id switch{"nord_white"=>[255,255,255,255],"nord_normal"=>[255,128,128,255],_=>[0,0,0,255]};
            }
            dds.ToPssgElement(texture);texture.AutoMipMap=false;
            if(texture.TexelFormat is not ("dxt1" or "dxt3" or "dxt5" or "ui8x4"))throw new InvalidDataException("Unsupported target texture format: "+texture.Id+" "+texture.TexelFormat);
        }
        Directory.CreateDirectory(Path.Combine(output,"shared"));
        Files.Save(textureSource,Path.Combine(output,"shared/tracksplit.pssg"));
        roots=route.Elements<PssgNode>().Where(n=>n.Id.StartsWith("ROOT_",StringComparison.Ordinal)).Reverse().ToArray();
        var after=new Bounds[parts.Count];
        var segmentLib=route.Elements<PssgLibrary>().Single(l=>l.Type=="SEGMENTSET");
        var dataLib=route.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");var state=new State();int meshId=0,depthTriangles=0;
        for(int t=0;t<parts.Count;t++)
        {
            var root=roots[t];var high=root.ChildElements.OfType<PssgRenderNode>().Single(n=>n.Id.StartsWith("HIGH_",StringComparison.Ordinal));
            var low=root.ChildElements.OfType<PssgRenderNode>().Single(n=>n.Id.StartsWith("LOW_",StringComparison.Ordinal));
            var bounds=new Bounds(new(float.MaxValue),new(float.MinValue));
            foreach(var mesh in scene.FullCourse?VisualBatches.Combine(parts[t]):parts[t])
            {
                string id="nord_RDS_"+meshId++;var writer=new RenderDataSourceWriter(id);
                var grass=GrassMaterial.Read(mesh.Material);
                for(int i=0;i<mesh.Positions.Length;i++)
                {
                    var p=mesh.Positions[i];bounds=bounds.Union(new(p,p));writer.Positions.Add(p);writer.Normals.Add(mesh.Normals[i]);
                    writer.Tangents.Add(new(mesh.Tangents[i],1));writer.Colors.Add(Vector4.One);
                    writer.TexCoords0.Add(grass?.UV(p,scene.Origin,mesh.Positions[0])??mesh.UV[i]);writer.TexCoords1.Add(mesh.UV[i]);writer.TexCoords2.Add(mesh.UV[i]);writer.TexCoords3.Add(mesh.UV[i]);
                }
                writer.Indices.AddRange(mesh.Indices.Select(i=>(uint)i));
                var segment=new PssgSegmentSet(route,segmentLib){Id=id+"_segments",SegmentCount=1};segmentLib.AppendChild(segment);
                writer.Write(layouts[Target(mesh.Material)],segment,dataLib,state);segment.Segments.Single().Primitive="triangles";
                foreach(var node in new[]{high,low})
                {
                    var instance=new PssgRenderStreamInstance(route,node){Id=id+"_"+node.Id,SourceCount=1,Indices="#"+id,StreamCount=0,Shader="#"+ids[mesh.Material.Id]};
                    node.AppendChild(instance);instance.AppendChild(new PssgRenderInstanceSource(route,instance){Source="#"+id});
                }
                // DiRT 2 submits terrain colour and opaque depth through separate named lists.
                // Cutout foliage and glass must retain their own alpha-aware passes.
                if(IsOpaque(mesh.Material))
                {
                    string depthId=id.Replace("nord_","nord_depth_",StringComparison.Ordinal);
                    var depthWriter=new RenderDataSourceWriter(depthId);
                    depthWriter.Positions.AddRange(mesh.Positions);depthWriter.Indices.AddRange(writer.Indices);
                    var depthSet=new PssgSegmentSet(route,segmentLib){Id=depthId+"_segments",SegmentCount=1};segmentLib.AppendChild(depthSet);
                    depthWriter.Write(depthLayout,depthSet,dataLib,state);depthSet.Segments.Single().Primitive="triangles";
                    foreach(var node in new[]{high,low})
                    {
                        var batch=root.ChildElements.OfType<PssgRenderNode>().Single(n=>n.Id==node.Id.Insert(node.Id.IndexOf('_'),"BATCH"));
                        var instance=new PssgRenderStreamInstance(route,batch){Id=depthId+"_"+batch.Id,SourceCount=1,Indices="#"+depthId,StreamCount=0,Shader="#batchmaterial"};
                        batch.AppendChild(instance);instance.AppendChild(new PssgRenderInstanceSource(route,instance){Source="#"+depthId});
                    }
                    depthTriangles+=mesh.Indices.Length/3;
                }
            }
            after[t]=bounds;
            foreach(var node in root.ChildElements.OfType<PssgRenderNode>())
            {
                node.Transform.Transform=Matrix4x4.Identity;node.BoundingBox.BoundsMin=bounds.Min;node.BoundingBox.BoundsMax=bounds.Max;
            }
            root.BoundingBox.BoundsMin=bounds.Min;root.BoundingBox.BoundsMax=bounds.Max;
        }
        foreach(var node in route.Elements<PssgNode>().Reverse())
        {
            var children=node.ChildElements.OfType<PssgNode>().ToArray();if(children.Length==0)continue;
            var bounds=children.Select(c=>new Bounds(c.BoundingBox.BoundsMin,c.BoundingBox.BoundsMax)).Aggregate((a,b)=>a.Union(b));
            node.BoundingBox.BoundsMin=bounds.Min;node.BoundingBox.BoundsMax=bounds.Max;
        }
        Files.Save(route,Path.Combine(output,"routesplit.pssg"));
        TerrainPasses.Validate(Files.Pssg(Path.Combine(output,"routesplit.pssg")),parts.Count);
        Files.Json(Path.Combine(output,"materials.json"),shaderMappings);
        Files.Json(Path.Combine(output,"visuals.json"),new{Meshes=meshId,Tiles=parts.Count,InitialTileSizeMetres=tileSize,DisjointSplitsPopulateAllVisibilityIdentities=!independentTiles,IndependentSpatialTiles=independentTiles,MaterialBatches=scene.FullCourse,TriangleCount=scene.Visuals.Sum(m=>m.Indices.Length/3),OpaqueDepthTriangles=depthTriangles,TextureContainer="shared/tracksplit.pssg",UniqueSourceTextures=textureBytes.Count,SourceTextureBytes=textureBytes.Values.Sum(b=>(long)b.Length),HighestDetailInBothLevels=true,RuntimeValidated=false});
        return(before,after);
    }
}
