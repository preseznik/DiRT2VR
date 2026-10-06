using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

namespace DiRT2VR.Nordschleife;

internal static class TerrainPasses
{
    internal static void Validate(PssgFile file,int expectedTiles=25)
    {
        var roots=file.Elements<PssgNode>().Where(n=>n.Id.StartsWith("ROOT_",StringComparison.Ordinal)).ToArray();
        if(roots.Length!=expectedTiles || expectedTiles is <1 or >Tiles.NativeLimit)throw new InvalidDataException("Terrain inventory does not match the native visibility capacity.");
        foreach(var root in roots)
        foreach(string prefix in new[]{"HIGH_","LOW_"})
        {
            var nodes=root.ChildElements.OfType<PssgRenderNode>().Where(n=>n.Id==prefix+root.Id[5..]).ToArray();
            if(nodes.Length!=1||!nodes[0].RenderInstances.Any())throw new InvalidDataException("Empty or missing native terrain identity: "+root.Id);
        }
        foreach(var input in file.Elements<PssgShaderInput>().Where(i=>i.Attributes.Any(a=>a.Name=="texture")))
            if(!input.Attributes.Any(a=>a.Name=="type"&&a.DisplayValue=="texture"))
                throw new InvalidDataException("Shader texture input has no native texture type.");
        foreach(var colour in file.Elements<PssgRenderNode>().Where(n=>n.Id.StartsWith("HIGH_",StringComparison.Ordinal)||n.Id.StartsWith("LOW_",StringComparison.Ordinal)))
        {
            var batch=colour.ParentElement!.ChildElements.OfType<PssgRenderNode>().Single(n=>n.Id==colour.Id.Insert(colour.Id.IndexOf('_'),"BATCH"));
            var expected=Faces(colour.RenderInstances.Where(IsOpaque));
            var actual=Faces(batch.RenderInstances);
            if(expected.Count!=actual.Count||expected.Any(f=>actual.GetValueOrDefault(f.Key)!=f.Value))
                throw new InvalidDataException("Terrain colour/depth coverage mismatch: "+colour.Id);
            foreach(var draw in batch.RenderInstances)
            {
                if(draw.GetShaderInstance().ShaderGroup!="#batched_track.fx")throw new InvalidDataException("Invalid opaque depth shader.");
                var source=draw.GetRenderDataSource();var stream=source.Streams.Single();var field=stream.GetDataBlock().Streams.ElementAt((int)stream.SubStream);
                if(field.RenderType!="Vertex"||field.DataType!="float3"||field.Stride!=12||field.Offset!=0)
                    throw new InvalidDataException("Depth pass requires the stock position-only vertex declaration.");
            }
        }
    }
    static bool IsOpaque(PssgRenderStreamInstance draw)
    {
        var material=draw.GetShaderInstance();
        if(material.ShaderGroup=="#terrain_infield.fx")return true;
        if(material.ShaderGroup!="#object_simple.fx")return false;
        var definitions=material.GetShaderGroup().InputDefinitions.ToArray();
        int diffuse=Array.FindIndex(definitions,d=>d.InputName=="TDiffuseAlphaMap");
        return material.Inputs.Single(i=>i.ParameterId==diffuse).Texture.EndsWith("_opaque",StringComparison.Ordinal);
    }
    static Dictionary<GeometryCoverage.ExactFace,int> Faces(IEnumerable<PssgRenderStreamInstance> draws)
    {
        var result=new Dictionary<GeometryCoverage.ExactFace,int>();
        foreach(var draw in draws)
        {
            var data=new RenderDataSourceReader(draw.GetRenderDataSource());
            if(data.IndexCount%3!=0)throw new InvalidDataException("Incomplete depth triangle.");
            for(int i=0;i<data.IndexCount;i+=3)
            {
                var face=GeometryCoverage.Exact(new(data.GetPosition(data.GetIndex(i)),data.GetPosition(data.GetIndex(i+1)),data.GetPosition(data.GetIndex(i+2)),"opaque"));
                result[face]=result.GetValueOrDefault(face)+1;
            }
        }
        return result;
    }
}
