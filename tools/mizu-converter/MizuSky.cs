using System.Numerics;
using System.Buffers.Binary;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class MizuSky
{
    internal static void Write(Scene scene,string grid2,string donor,string output)
    {
        string source=Files.Inside(grid2,"tracks/locations/p2p/okutama/route_0/sky_day.pssg");
        string donorSky=Files.Inside(donor,"sky.pssg");
        Files.NoLinks(source);Files.NoLinks(donorSky);
        scene.Inputs[source]=Files.Hash(source);scene.Inputs[donorSky]=Files.Hash(donorSky);
        PreserveSource(source,donorSky,output,closeBottom:true,candidate:true);
        File.Copy(Path.Combine(output,"sky.pssg"),Path.Combine(output,"shared/sky.pssg"));
    }
    sealed class State:PssgModelWriterState { }
    internal static void PreserveSource(string sourcePath,string donorPath,string output,bool extendBelowHorizon=false,bool closeBottom=false,bool candidate=false)
    {
        if(!candidate)Files.NewOutput(output,sourcePath,donorPath);
        if(File.Exists(Path.Combine(output,"sky.pssg")))throw new InvalidDataException("Existing sky export preserved");
        var source=Files.Pssg(sourcePath);var donor=Files.Pssg(donorPath);
        var doc=Files.Document(source);var template=Files.Document(donor);
        var group=template.Descendants("SHADERGROUP").Single();
        doc.Descendants("SHADERGROUP").Single().ReplaceWith(new XElement(group));
        foreach(var shader in doc.Descendants("SHADERINSTANCE"))
        {
            // The source has texture at 0 and texcoord at 1. DiRT 2 inserts
            // exposure at 1; preserve raw vertex/index buffers and remap inputs.
            foreach(var input in shader.Elements("SHADERINPUT"))if((int)input.Attribute("parameterID")!>=1)input.SetAttributeValue("parameterID",(int)input.Attribute("parameterID")!+1);
            shader.Add(new XElement(template.Descendants("SHADERINPUT").Single(i=>(int)i.Attribute("parameterID")! ==1)));
            shader.SetAttributeValue("parameterCount",3);shader.SetAttributeValue("parameterSavedCount",shader.Elements("SHADERINPUT").Count());
        }
        var adapted=Files.FromDocument(doc);
        int capTriangles=closeBottom?CloseBottom(adapted):0;
        if(extendBelowHorizon)
        {
            // A bounded diagnostic for the open dome at downhill viewpoints.
            // Keep native source streams and UVs; extend only negative Y.
            foreach(var block in adapted.Elements<PssgDataBlock>())
                foreach(var stream in block.Streams.Where(s=>s.RenderType=="Vertex"))
                {
                    if(stream.DataType!="float3")throw new InvalidDataException("Sky skirt requires float3 positions");
                    for(uint i=0;i<block.ElementCount;i++)
                    {
                        var value=block.Data.Value.AsSpan(checked((int)(stream.Offset+i*stream.Stride)+4),4);
                        float y=BinaryPrimitives.ReadSingleBigEndian(value);
                        if(y<0)BinaryPrimitives.WriteSingleBigEndian(value,y*6);
                    }
                }
            var positions=new RenderDataSourceReader(adapted.Elements<PssgRenderStreamInstance>().Single().GetRenderDataSource());
            foreach(var node in adapted.Elements<PssgRenderNode>())
            {
                var values=Enumerable.Range(0,(int)positions.VertexCount).Select(i=>positions.GetPosition((uint)i)).ToArray();
                node.BoundingBox.BoundsMin=values.Aggregate(Vector3.Min);node.BoundingBox.BoundsMax=values.Aggregate(Vector3.Max);
            }
        }
        string file=Path.Combine(output,"sky.pssg");Files.Save(adapted,file);
        var a=new RenderDataSourceReader(source.Elements<PssgRenderStreamInstance>().Single().GetRenderDataSource());
        var b=new RenderDataSourceReader(Files.Pssg(file).Elements<PssgRenderStreamInstance>().Single().GetRenderDataSource());
        Vector3 Expected(uint i){var p=a.GetPosition(i);if(extendBelowHorizon&&p.Y<0)p.Y*=6;return p;}
        if(a.VertexCount+capTriangles!=b.VertexCount||a.IndexCount+capTriangles*3!=b.IndexCount||Enumerable.Range(0,(int)a.VertexCount).Any(i=>Expected((uint)i)!=b.GetPosition((uint)i)||a.GetTexCoord((uint)i,0)!=b.GetTexCoord((uint)i,0))||Enumerable.Range(0,(int)a.IndexCount).Any(i=>a.GetIndex(i)!=b.GetIndex(i)))throw new InvalidDataException("Sky raw-buffer preservation failed");
        if(!extendBelowHorizon)
        {
            var originalBytes=source.Elements<PssgDataBlock>().Single().Data.Value;
            var outputBytes=Files.Pssg(file).Elements<PssgDataBlock>().Single().Data.Value;
            var originalIndices=source.Elements<PssgRenderStreamInstance>().Single().GetRenderDataSource().IndexSource!.Data.Value;
            var outputIndices=Files.Pssg(file).Elements<PssgRenderStreamInstance>().Single().GetRenderDataSource().IndexSource!.Data.Value;
            if(!originalBytes.AsSpan().SequenceEqual(outputBytes.AsSpan(0,originalBytes.Length))||!originalIndices.AsSpan().SequenceEqual(outputIndices.AsSpan(0,originalIndices.Length)))throw new InvalidDataException("Source sky buffer prefixes changed");
        }
        Files.Json(Path.Combine(output,candidate?"sky-adapter.json":"sky-probe.json"),new{SourcePath=Path.GetFullPath(sourcePath),SourceHash=Files.Hash(sourcePath),DonorPath=Path.GetFullPath(donorPath),DonorHash=Files.Hash(donorPath),OutputHash=Files.Hash(file),SourceVertexCount=a.VertexCount,SourceIndexCount=a.IndexCount,SourcePositionsUVsAndIndicesExact=!extendBelowHorizon,RawSourceBuffersPreserved=!extendBelowHorizon&&!closeBottom,OriginalBufferPrefixesExact=!extendBelowHorizon,NativeSourceStreamsUVsAndIndicesPreserved=true,ExtendedBelowHorizon=extendBelowHorizon,CapTriangles=capTriangles,RuntimeValidated=false});
    }
    static int CloseBottom(PssgFile file)
    {
        var rds=file.Elements<PssgRenderStreamInstance>().Single().GetRenderDataSource();
        var reader=new RenderDataSourceReader(rds);
        var block=file.Elements<PssgDataBlock>().Single();
        var streams=block.Streams.ToArray();
        if(streams.Length!=2||streams[0].RenderType!="Vertex"||streams[0].DataType!="float3"||streams[0].Offset!=0||streams[0].Stride!=16||streams[1].RenderType!="ST"||streams[1].DataType!="half2"||streams[1].Offset!=12||streams[1].Stride!=16||rds.IndexSource!.Format!="ushort")throw new InvalidDataException("Unsupported source sky cap layout");
        float bottom=Enumerable.Range(0,(int)reader.VertexCount).Min(i=>reader.GetPosition((uint)i).Y);
        var edges=new List<(uint A,uint B)>();
        foreach(var (a,b,c) in reader.GetTriangles())
            foreach(var edge in new[]{(A:a,B:b),(A:b,B:c),(A:c,B:a)})
                if(MathF.Abs(reader.GetPosition(edge.A).Y-bottom)<0.1f&&MathF.Abs(reader.GetPosition(edge.B).Y-bottom)<0.1f&&Vector3.DistanceSquared(reader.GetPosition(edge.A),reader.GetPosition(edge.B))>1)edges.Add(edge);
        var boundaryVertices=edges.SelectMany(e=>new[]{e.A,e.B}).Select(i=>reader.GetPosition(i)).GroupBy(p=>(MathF.Round(p.X,1),MathF.Round(p.Z,1)));
        if(edges.Count!=30||boundaryVertices.Any(g=>g.Count()!=2))throw new InvalidDataException("Source sky bottom is not the expected closed 30-edge boundary");
        var vertices=new byte[block.Data.Value.Length+edges.Count*16];block.Data.Value.CopyTo(vertices,0);
        var indices=rds.IndexSource!.Data.Value.Concat(new byte[edges.Count*6]).ToArray();
        for(int i=0;i<edges.Count;i++)
        {
            var (a,b)=edges[i];int offset=block.Data.Value.Length+i*16;
            var uv=(reader.GetTexCoord(a,0)+reader.GetTexCoord(b,0))/2;
            BinaryPrimitives.WriteSingleBigEndian(vertices.AsSpan(offset+4),-20000);
            BinaryPrimitives.WriteHalfBigEndian(vertices.AsSpan(offset+12),(Half)uv.X);BinaryPrimitives.WriteHalfBigEndian(vertices.AsSpan(offset+14),(Half)uv.Y);
            int indexOffset=rds.IndexSource.Data.Value.Length+i*6;
            BinaryPrimitives.WriteUInt16BigEndian(indices.AsSpan(indexOffset),checked((ushort)b));
            BinaryPrimitives.WriteUInt16BigEndian(indices.AsSpan(indexOffset+2),checked((ushort)a));
            BinaryPrimitives.WriteUInt16BigEndian(indices.AsSpan(indexOffset+4),checked((ushort)(reader.VertexCount+i)));
            var p=reader.GetPosition(b);var q=reader.GetPosition(a);var centre=new Vector3(0,-20000,0);
            if(Vector3.Dot(Vector3.Cross(q-p,centre-p),(p+q+centre)/3)>=0)throw new InvalidDataException("Sky cap faces outwards");
        }
        block.Data.Value=vertices;block.Size=(uint)vertices.Length;block.ElementCount+=(uint)edges.Count;
        rds.IndexSource.Data.Value=indices;rds.IndexSource.Count+=(uint)edges.Count*3;rds.IndexSource.MaximumIndex=block.ElementCount-1;
        var node=file.Elements<PssgRenderNode>().Single();var minimum=node.BoundingBox.BoundsMin;minimum.Y=-20000;node.BoundingBox.BoundsMin=minimum;
        return edges.Count;
    }
}
