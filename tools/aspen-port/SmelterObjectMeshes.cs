using System.Xml.Linq;

// DiRT 2's object_simple and instanced variants consume position, two UVs and
// a float normal. Use one declaration for the entire shader, including damage
// meshes and terrain-baked copies; fixing individual models leaves mixed layouts.
internal static class SmelterObjectMeshes
{
    internal static object Convert(XDocument document)
    {
        var nodes=document.Descendants().Where(n=>n.Attribute("id") is not null).ToDictionary(n=>(string)n.Attribute("id")!);
        var draws=document.Descendants("RENDERSTREAMINSTANCE")
            .Where(d=>(string?)nodes[((string)d.Attribute("shader")!)[1..]].Attribute("shaderGroup")=="#object_simple.fx").ToArray();
        var sources=draws.Select(d=>((string)d.Element("RENDERINSTANCESOURCE")!.Attribute("source")!)[1..]).ToHashSet(StringComparer.Ordinal);
        var blocks=sources.SelectMany(id=>nodes[id].Elements("RENDERSTREAM")).Select(s=>((string)s.Attribute("dataBlock")!)[1..]).ToHashSet(StringComparer.Ordinal);
        if(sources.Count==0 || document.Descendants("RENDERSTREAMINSTANCE").Except(draws).Any(d=>sources.Contains(((string)d.Element("RENDERINSTANCESOURCE")!.Attribute("source")!)[1..])) ||
            document.Descendants("RENDERDATASOURCE").Where(n=>!sources.Contains((string)n.Attribute("id")!)).SelectMany(n=>n.Elements("RENDERSTREAM")).Any(s=>blocks.Contains(((string)s.Attribute("dataBlock")!)[1..])))
            throw new InvalidDataException("Simple object buffers are missing or shared with another shader.");
        int vertices=0;
        foreach(string id in blocks) {
            var fields=nodes[id].Elements("DATABLOCKSTREAM").ToArray();
            string signature=string.Join(';',fields.Select(f=>string.Join(':',new[]{"renderType","dataType","offset","stride"}.Select(a=>(string?)f.Attribute(a)))));
            if(signature is not ("Vertex:float3:0:32;Color:uint_color_argb:12:32;ST:half2:16:32;ST:half2:20:32;Normal:half4:24:32" or
                "Vertex:float3:0:32;ST:half2:12:32;ST:half2:16:32;Normal:float3:20:32"))
                throw new InvalidDataException("Unexpected Smelter simple-object declaration: "+id);
            vertices+=(int)nodes[id].Attribute("elementCount")!;
        }
        // The stock shader's stream definitions and all six passes have no COLOR
        // input. Retain positions and both UVs exactly; half normals expand losslessly.
        var subset=new XDocument(new XElement("PSSGFILE",blocks.Select(id=>new XElement(nodes[id])),sources.Select(id=>new XElement(nodes[id]))));
        ObjectVertexLayout.Convert(subset,keepColour:false);
        foreach(var node in subset.Root!.Elements()) nodes[(string)node.Attribute("id")!].ReplaceWith(new XElement(node));
        TreeVertexLayouts.Validate(document);
        return new { Shader="object_simple.fx",Draws=draws.Length,Buffers=blocks.Count,Vertices=vertices,
            NativeStride=32,PositionsUvsNormalsAndIndicesPreserved=true,UnusedColourStreamRemoved=true,RuntimeValidated=false };
    }

    internal static IReadOnlySet<string> NativeBatches(string track)
    {
        var objects=PortFiles.ReadPssg(Path.Combine(track,"objects.pssg"));
        var nodes=objects.Descendants().Where(n=>n.Attribute("id") is not null).ToDictionary(n=>(string)n.Attribute("id")!);
        var result=new HashSet<string>(SmelterBarrierMeshes.Models,StringComparer.Ordinal);
        foreach(string model in PortFiles.ReadPssg(Path.Combine(track,"route_0/objects.ens")).Descendants("TEMPLATEENTITYINSTANCE")
            .Select(e=>((string)e.Attribute("uri")!)[1..]).Distinct(StringComparer.Ordinal)) {
            var draws=nodes[model+" Root"].Descendants("RENDERSTREAMINSTANCE").ToArray();
            if(draws.Length>0 && draws.All(d=>(string?)nodes[((string)d.Attribute("shader")!)[1..]].Attribute("shaderGroup")=="#object_simple.fx"))
                result.Add(model);
        }
        return result;
    }
}
