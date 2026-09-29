using System.Globalization;
using System.Xml.Linq;

// Visible terrain and batched depth geometry are separate DiRT 2 passes.
internal static class TerrainDepth
{
    internal static object Convert(string track)
    {
        var report=new List<object>();
        foreach(var relative in new[]{"land.pssg","route_0/routesplit.pssg"})
        {
            var path=Path.Combine(track,relative);var doc=PortFiles.ReadPssg(path);
            int triangles=Add(doc);
            if(triangles>0) { PortFiles.WritePssg(doc,path+".tmp");ObjectVertexLayout.Verify(doc,PortFiles.ReadPssg(path+".tmp"));File.Move(path+".tmp",path,true); }
            report.Add(new{File=relative,DepthTriangles=triangles});
        }
        return new{Files=report,PositionsAndWindingPreserved=true,RuntimeValidated=false};
    }
    internal static int Add(XDocument doc)
    {
        var nodes=doc.Descendants().Where(n=>n.Attribute("id") is not null).ToDictionary(n=>(string)n.Attribute("id")!);
        XElement Library(string type) => doc.Descendants("LIBRARY").Single(n=>(string?)n.Attribute("type")==type);
        int triangles=0;
        foreach(var colour in doc.Descendants("RENDERNODE").Where(n=>((string?)n.Attribute("id")) is {} id && (id.StartsWith("HIGH_") || id.StartsWith("LOW_"))).ToArray())
        {
            string colourId=(string)colour.Attribute("id")!;
            // Only opaque reconstructed snow: fences and foliage need their
            // alpha-tested shader, not a solid position-only depth pass.
            var draws=colour.Elements("RENDERSTREAMINSTANCE").Where(n=>((string?)n.Attribute("id"))?.StartsWith("aspen_snow_",StringComparison.Ordinal)==true).ToArray();
            if(draws.Length==0) continue;
            string batchId=colourId.Insert(colourId.IndexOf('_'),"BATCH");
            var batch=colour.Parent!.Elements("RENDERNODE").SingleOrDefault(n=>(string?)n.Attribute("id")==batchId);
            if(batch is null) { batch=new XElement("RENDERNODE",new XAttribute("id",batchId),new XAttribute("nickname",batchId),new XAttribute("stopTraversal",0),new XElement(colour.Element("TRANSFORM")!),new XElement(colour.Element("BOUNDINGBOX")!));colour.AddAfterSelf(batch); }
            var material=doc.Descendants("SHADERINSTANCE").Single(n=>(string?)n.Attribute("id")=="batchmaterial" && (string?)n.Attribute("shaderGroup")=="#batched_track.fx");
            foreach(var draw in draws)
            {
                string id="aspen_depth_"+(string)draw.Attribute("id")!;
                if(nodes.ContainsKey(id)) throw new InvalidDataException("Converted depth mesh already present.");
                var source=nodes[((string)draw.Element("RENDERINSTANCESOURCE")!.Attribute("source")!)[1..]];
                var streams=source.Elements("RENDERSTREAM").Select(s=>(Block:nodes[((string)s.Attribute("dataBlock")!)[1..]],Index:(int)s.Attribute("subStream")!));
                var vertex=streams.Single(s=>(string?)s.Block.Elements("DATABLOCKSTREAM").ElementAt(s.Index).Attribute("renderType")=="Vertex");
                var field=vertex.Block.Elements("DATABLOCKSTREAM").ElementAt(vertex.Index);
                if((string?)field.Attribute("dataType")!="float3" || (string?)source.Attribute("primitive")!="triangles") throw new InvalidDataException("Unsupported converted depth source.");
                int count=(int)vertex.Block.Attribute("elementCount")!,stride=(int)field.Attribute("stride")!,offset=(int)field.Attribute("offset")!;
                var bytes=System.Convert.FromHexString(string.Concat(vertex.Block.Element("DATABLOCKDATA")!.Value.Where(c=>!char.IsWhiteSpace(c))));
                if(count<1 || stride<12 || offset<0 || (long)(count-1)*stride+offset+12>bytes.Length) throw new InvalidDataException("Truncated depth positions.");
                var positions=new byte[checked(count*12)];
                for(int i=0;i<count;i++) bytes.AsSpan(i*stride+offset,12).CopyTo(positions.AsSpan(i*12));
                var block=new XElement("DATABLOCK",new XAttribute("id",id+"_vertices"),new XAttribute("streamCount",1),new XAttribute("elementCount",count),new XAttribute("size",positions.Length),
                    new XElement("DATABLOCKSTREAM",new XAttribute("renderType","Vertex"),new XAttribute("dataType","float3"),new XAttribute("offset",0),new XAttribute("stride",12)),new XElement("DATABLOCKDATA",EgoEngineLibrary.Helper.HexHelper.ByteArrayToHexViaLookup32(positions)));
                Library("RENDERINTERFACEBOUND").Add(block);
                var indices=new XElement(source.Element("RENDERINDEXSOURCE")!);indices.SetAttributeValue("id",id+"_indices");
                var values=indices.Element("INDEXSOURCEDATA")!.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                if(values.Length!=(int)indices.Attribute("count")! || values.Length%3!=0 || values.Any(i=>i<0 || i>=count)) throw new InvalidDataException("Invalid depth indices.");
                var mesh=new XElement("RENDERDATASOURCE",new XAttribute("id",id),new XAttribute("primitive","triangles"),new XAttribute("streamCount",1),indices,
                    new XElement("RENDERSTREAM",new XAttribute("id",id+"_stream"),new XAttribute("dataBlock","#"+id+"_vertices"),new XAttribute("subStream",0)));
                Library("SEGMENTSET").Add(new XElement("SEGMENTSET",new XAttribute("id",id+"_segment"),new XAttribute("segmentCount",1),mesh));
                batch.Add(new XElement("RENDERSTREAMINSTANCE",new XAttribute("id",id+"_draw"),new XAttribute("sourceCount",1),new XAttribute("streamCount",0),new XAttribute("indices","#"+id),new XAttribute("shader","#"+(string)material.Attribute("id")!),new XElement("RENDERINSTANCESOURCE",new XAttribute("source","#"+id))));
                triangles+=values.Length/3;
            }
            float[] Bounds(XElement n)=>n.Element("BOUNDINGBOX")!.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
            var a=Bounds(batch);var b=Bounds(colour);
            batch.Element("BOUNDINGBOX")!.Value=string.Join(' ',Enumerable.Range(0,6).Select(i=>(i<3?Math.Min(a[i],b[i]):Math.Max(a[i],b[i])).ToString("R",CultureInfo.InvariantCulture)));
        }
        return triangles;
    }
}
