using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class SpaDetailAppend
{
    sealed class State:PssgModelWriterState { }
    static XElement Xml(PssgElement e){var r=new XElement("root");e.WriteXml(r);return r.Elements().Single();}
    internal static void Append(string candidate,string output,Scene scene,string prefix="spa_grass")
    {
        var original=Files.Pssg(Files.Inside(candidate,"routesplit.pssg"));
        var route=Files.Pssg(Files.Inside(candidate,"routesplit.pssg"));var textures=Files.Pssg(Files.Inside(candidate,"shared/tracksplit.pssg"));
        var group=route.GetObject<PssgShaderGroup>("object_simple.fx".AsMemory());
        var template=route.Elements<PssgShaderInstance>().First(s=>s.GetShaderGroup().Id==group.Id);
        var layout=ShaderInputInfo.CreateFromPssg(route).Single(l=>l.ShaderGroupId==group.Id);
        var textureLibrary=textures.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");
        var textureIds=new Dictionary<string,string>();
        foreach(var (key,texture) in scene.Textures){string id=prefix+"_tex_"+texture.Hash[..20];textureIds.Add(key,id);var next=(PssgTexture)textures.GetObject<PssgTexture>("nord_white".AsMemory()).DeepClone();next.Id=id;textureLibrary.AppendChild(next);using var memory=new MemoryStream(texture.Dds);new DdsFile(memory).ToPssgElement(next);next.AutoMipMap=false;next.AddAttribute("minFilter",5);next.AddAttribute("magFilter",5);using var check=new MemoryStream();next.ToDdsFile().Write(check);if(!check.ToArray().AsSpan().SequenceEqual(texture.Dds))throw new InvalidDataException("Source detail texture changed");}
        var definitions=group.InputDefinitions.ToArray();var shaderLibrary=route.Elements<PssgLibrary>().Single(l=>l.Type=="SHADERINSTANCE");var ids=new Dictionary<string,string>();
        foreach(var material in scene.Visuals.Select(m=>m.Material).DistinctBy(m=>m.Id)){
            if(!material.AlphaTest||Visuals.Target(material)!=group.Id)throw new InvalidDataException("Only source cutout details are supported");
            string id=prefix+"_material_"+ids.Count;ids.Add(material.Id,id);var shader=new PssgShaderInstance(route,shaderLibrary){Id=id,ShaderGroup="#"+group.Id,ParameterCount=template.ParameterCount,ParameterSavedCount=template.ParameterSavedCount,RenderSortPriority=template.RenderSortPriority};shaderLibrary.AppendChild(shader);
            foreach(var input in template.Inputs){var next=new PssgShaderInput(route,shader){ParameterId=input.ParameterId,Type=input.Type,Value=(byte[])input.Value.Clone()};if(input.Type=="texture")next.Texture=definitions[input.ParameterId].InputName=="TDiffuseAlphaMap"?"tracksplit.pssg#"+textureIds[material.Textures["txDiffuse"]]:definitions[input.ParameterId].InputName=="TOcclusionMap"?"tracksplit.pssg#nord_white":definitions[input.ParameterId].InputName=="TNormalMap"?"tracksplit.pssg#nord_normal":"tracksplit.pssg#nord_black";else next.Format=input.Format;shader.AppendChild(next);}
        }
        var data=route.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");var sets=route.Elements<PssgLibrary>().Single(l=>l.Type=="SEGMENTSET");
        uint start=checked((uint)(route.Elements<PssgObject>().Select(o=>System.Text.RegularExpressions.Regex.Match(o.Id,@"(\d+)$")).Where(m=>m.Success).Select(m=>int.Parse(m.Value)).Max()+1));var state=new State{DataBlockCount=start,RenderStreamCount=start};
        var roots=route.Elements<PssgNode>().Where(n=>n.Id.StartsWith("ROOT_",StringComparison.Ordinal)).ToArray();
        PssgNode Tile(Mesh mesh){var centre=mesh.Positions.Aggregate(Vector3.Zero,(a,p)=>a+p)/mesh.Positions.Length;return roots.MinBy(r=>Vector2.DistanceSquared(new((r.BoundingBox.BoundsMin.X+r.BoundingBox.BoundsMax.X)/2,(r.BoundingBox.BoundsMin.Z+r.BoundingBox.BoundsMax.Z)/2),new(centre.X,centre.Z)))!;}
        int batches=0;
        foreach(var tileGroup in scene.Visuals.GroupBy(Tile))foreach(var mesh in VisualBatches.Combine(tileGroup)){
            string id=prefix+"_RDS_"+batches++;var writer=new RenderDataSourceWriter(id);
            for(int i=0;i<mesh.Positions.Length;i++){writer.Positions.Add(mesh.Positions[i]);writer.Normals.Add(mesh.Normals[i]);writer.Tangents.Add(new(mesh.Tangents[i],1));writer.Colors.Add(Vector4.One);writer.TexCoords0.Add(mesh.UV[i]);writer.TexCoords1.Add(mesh.UV[i]);}
            writer.Indices.AddRange(mesh.Indices.Select(i=>(uint)i));var set=new PssgSegmentSet(route,sets){Id=id+"_segments",SegmentCount=1};sets.AppendChild(set);writer.Write(layout,set,data,state);set.Segments.Single().Primitive="triangles";
            route.Elements<PssgRenderDataSource>().Single(r=>r.Id==id).IndexSource!.Id=id+"_indices";
            var tile=tileGroup.Key;var bounds=new Bounds(tile.BoundingBox.BoundsMin,tile.BoundingBox.BoundsMax);foreach(var p in mesh.Positions)bounds=bounds.Union(new(p,p));
            foreach(var node in tile.ChildElements.OfType<PssgRenderNode>()){
                node.BoundingBox.BoundsMin=bounds.Min;node.BoundingBox.BoundsMax=bounds.Max;
                if(!node.Id.StartsWith("HIGH_",StringComparison.Ordinal)&&!node.Id.StartsWith("LOW_",StringComparison.Ordinal))continue;
                var draw=new PssgRenderStreamInstance(route,node){Id=id+"_"+node.Id,SourceCount=1,Indices="#"+id,StreamCount=0,Shader="#"+ids[mesh.Material.Id]};node.AppendChild(draw);draw.AppendChild(new PssgRenderInstanceSource(route,draw){Source="#"+id});
            }
            tile.BoundingBox.BoundsMin=bounds.Min;tile.BoundingBox.BoundsMax=bounds.Max;
        }
        foreach(var node in route.Elements<PssgNode>().Reverse()){var children=node.ChildElements.OfType<PssgNode>().ToArray();if(children.Length>0){node.BoundingBox.BoundsMin=children.Select(c=>c.BoundingBox.BoundsMin).Aggregate(Vector3.Min);node.BoundingBox.BoundsMax=children.Select(c=>c.BoundingBox.BoundsMax).Aggregate(Vector3.Max);}}
        if(route.Elements<PssgObject>().GroupBy(o=>o.Id).Any(g=>g.Count()>1))throw new InvalidDataException("Duplicate detail object identity");
        if(route.Elements<PssgRenderDataSource>().Count()>4160)throw new InvalidDataException("Details exceed the measured render source budget");
        Files.Save(route,Path.Combine(output,"routesplit.pssg"));Files.Save(textures,Path.Combine(output,"tracksplit.pssg"));
        var saved=Files.Pssg(Path.Combine(output,"routesplit.pssg"));var objects=saved.Elements<PssgObject>().ToDictionary(o=>o.Id);
        foreach(var o in original.Elements<PssgObject>().Where(o=>o is not PssgNode))if(!XNode.DeepEquals(Xml(o),Xml(objects[o.Id])))throw new InvalidDataException("Original visual payload/reference changed: "+o.Id);
        foreach(var node in original.Elements<PssgNode>()){var next=(PssgNode)objects[node.Id];if(node.Transform.Transform!=next.Transform.Transform||(node.ParentElement as PssgObject)?.Id!=(next.ParentElement as PssgObject)?.Id)throw new InvalidDataException("Original node placement changed");}
        foreach(var (path,hash) in scene.Inputs)if(Files.Hash(path)!=hash)throw new InvalidDataException("Source changed during detail conversion");
        Files.Json(Path.Combine(output,"detail-append.json"),new{SourceCandidate=Path.GetFullPath(candidate),Batches=batches,Meshes=scene.Visuals.Count,Triangles=scene.Visuals.Sum(m=>m.Indices.Length/3),OriginalDrawsAndGeometryUnchanged=true,BoundsExpandedOnly=true,NewCollision=false,NativeRuntimeValidated=false});
        Files.Json(Path.Combine(output,"car-lighting-probe.json"),new{SourceCandidate=Path.GetFullPath(candidate),RenderingProbeOnly=true,Purpose="Spa source grass cards",Files=new[]{"routesplit.pssg","tracksplit.pssg"}.ToDictionary(n=>n,n=>Files.Hash(Path.Combine(output,n)))});
        Console.WriteLine($"Source details appended in {batches} batches.");
    }
}
