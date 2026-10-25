using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Run after movable furniture has removed its baked colour and depth copies.
// Reconstruct only opaque position data; alpha and animated passes stay intact.
internal static class PikesDepthBatching
{
    sealed class State:PssgModelWriterState { }
    internal sealed record Result(int Before,int After,int Tiles,bool ExactCoverage);
    internal static Result Apply(PssgFile file)
    {
        var layout=ShaderInputInfo.CreateFromPssg(file).Single(s=>s.ShaderGroupId=="batched_track.fx");
        var roots=file.Elements<PssgNode>().Where(n=>n.Id.StartsWith("ROOT_",StringComparison.Ordinal)).ToArray();
        var data=file.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");
        var sets=file.Elements<PssgLibrary>().Single(l=>l.Type=="SEGMENTSET");
        uint Next(IEnumerable<string> ids,string prefix)=>ids.Where(s=>s.StartsWith(prefix,StringComparison.Ordinal)).Select(s=>uint.Parse(s[prefix.Length..])).DefaultIfEmpty().Max()+1;
        var state=new State{DataBlockCount=Next(file.Elements<PssgDataBlock>().Select(b=>b.Id),"block"),RenderStreamCount=Next(file.Elements<PssgRenderStream>().Select(b=>b.Id),"stream")};
        var old=file.Elements<PssgRenderDataSource>().Where(s=>s.Id.StartsWith("nord_depth_RDS_",StringComparison.Ordinal)||s.Id.StartsWith("pikes_depth_RDS_",StringComparison.Ordinal)).ToHashSet();
        var blocks=old.SelectMany(s=>s.Streams.Select(v=>v.GetDataBlock())).ToHashSet();
        var shared=file.Elements<PssgRenderDataSource>().Where(s=>!old.Contains(s)).SelectMany(s=>s.Streams.Select(v=>v.GetDataBlock())).ToHashSet();
        if(blocks.Overlaps(shared))throw new InvalidDataException("Opaque depth shares a colour vertex buffer");
        int before=0,after=0,id=checked((int)Next(old.Select(s=>s.Id),"pikes_depth_RDS_"));
        foreach(var root in roots)
        {
            string suffix=root.Id[5..];
            PssgRenderNode Node(string prefix)=>root.ChildElements.OfType<PssgRenderNode>().Single(n=>n.Id==prefix+suffix);
            PssgRenderNode high=Node("HIGH_"),low=Node("LOW_"),highBatch=Node("HIGHBATCH_"),lowBatch=Node("LOWBATCH_");
            foreach(var batch in new[]{highBatch,lowBatch})
            foreach(var draw in batch.RenderInstances.Where(d=>d.GetShaderInstance().ShaderGroup=="#batched_track.fx").ToArray())
            {
                if(!old.Contains(draw.GetRenderDataSource()))throw new InvalidDataException("Unexpected Pike's depth source");
                if(batch==highBatch)before++;batch.RemoveChild(draw);
            }
            var highSources=high.RenderInstances.Where(TerrainPasses.IsOpaque).Select(d=>d.GetRenderDataSource()).ToArray();
            var lowSources=low.RenderInstances.Where(TerrainPasses.IsOpaque).Select(d=>d.GetRenderDataSource()).ToArray();
            if(highSources.Distinct().Count()!=highSources.Length||lowSources.Distinct().Count()!=lowSources.Length)throw new InvalidDataException("Repeated opaque terrain instance");
            foreach(var group in highSources.Union(lowSources).GroupBy(s=>(High:highSources.Contains(s),Low:lowSources.Contains(s))))
            {
                RenderDataSourceWriter? writer=null;
                void Flush()
                {
                    if(writer is null)return;
                    var set=new PssgSegmentSet(file,sets){Id=writer.Name+"_segments",SegmentCount=1};sets.AppendChild(set);
                    writer.Write(layout,set,data,state);set.Segments.Single().Primitive="triangles";
                    foreach(var node in new[]{highBatch,lowBatch}.Where(n=>n==highBatch?group.Key.High:group.Key.Low))
                    {
                        var draw=new PssgRenderStreamInstance(file,node){Id=writer.Name+"_"+node.Id,SourceCount=1,Indices="#"+writer.Name,StreamCount=0,Shader="#batchmaterial"};
                        node.AppendChild(draw);draw.AppendChild(new PssgRenderInstanceSource(file,draw){Source="#"+writer.Name});
                        if(node==highBatch)after++;
                    }
                }
                foreach(var source in group)
                {
                    var reader=new RenderDataSourceReader(source);
                    if(reader.VertexCount>65536||reader.Primitive!="triangles")throw new InvalidDataException("Unsupported opaque terrain topology");
                    if(writer is null||writer.Positions.Count+reader.VertexCount>65536){Flush();writer=new("pikes_depth_RDS_"+id++);}
                    uint offset=(uint)writer.Positions.Count;
                    for(uint i=0;i<reader.VertexCount;i++)writer.Positions.Add(reader.GetPosition(i));
                    for(int i=0;i<reader.IndexCount;i++)writer.Indices.Add(checked(offset+reader.GetIndex(i)));
                }
                Flush();
            }
        }
        foreach(var source in old){var parent=source.ParentElement!;parent.RemoveChild(source);if(parent is PssgSegmentSet&&!parent.ChildElements.Any())parent.ParentElement!.RemoveChild(parent);}
        foreach(var block in blocks)block.ParentElement!.RemoveChild(block);
        TerrainPasses.Validate(file,roots.Length);
        return new(before,after,roots.Length,true);
    }
}
