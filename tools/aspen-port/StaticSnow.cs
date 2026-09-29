using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

// DiRT 3's interactive snow includes the actual road surface, not just an effect.
// Preserve its base triangles as ordinary high-detail terrain; deformation stays unsupported.
internal static class StaticSnow
{
    static float[] Values(string text) => text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
    static string Text(IEnumerable<float> values) => string.Join(' ',values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
    static byte[] Bytes(XElement block) => Convert.FromHexString(string.Concat(block.Element("DATABLOCKDATA")!.Value.Where(c=>!char.IsWhiteSpace(c))));
    static Vector3 Position(byte[] bytes,int vertex) => new(BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(vertex*52)),BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(vertex*52+4)),BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(vertex*52+8)));

    internal static void ConvertVertices(XElement block)
    {
        var fields=block.Elements("DATABLOCKSTREAM").ToArray();
        var expected=new[]{("Vertex","float3",0),("Color","uint_color_argb",12),("Normal","float4",16),("Tangent","float4",32),("ST","float4",48)};
        if(fields.Length!=expected.Length || fields.Where((f,i)=>(string?)f.Attribute("renderType")!=expected[i].Item1 || (string?)f.Attribute("dataType")!=expected[i].Item2 || (int?)f.Attribute("offset")!=expected[i].Item3 || (int?)f.Attribute("stride")!=64).Any())
            throw new InvalidDataException("Unsupported interactive-snow vertex layout.");
        int count=(int)block.Attribute("elementCount")!; var source=Bytes(block);
        if(source.Length!=checked(count*64)) throw new InvalidDataException("Truncated snow vertex block.");
        var result=new byte[checked(count*52)];
        for(int i=0;i<count;i++)
        {
            var from=source.AsSpan(i*64,64); var to=result.AsSpan(i*52,52);
            from[..12].CopyTo(to); // Exact authored positions, without tessellation displacement.
            from.Slice(12,4).CopyTo(to[12..]); to[12]=255; to[14]=0; to[15]=0;
            // Match DiRT 2's terrain declaration, including its packed half fields.
            foreach(var (fromOffset,toOffset) in new[]{(48,16),(52,18),(48,20),(52,22),(56,24),(60,26),(16,28),(20,30),(24,32),(32,36),(36,38),(40,40)})
            {
                float value=BinaryPrimitives.ReadSingleBigEndian(from[fromOffset..]);
                if(!float.IsFinite(value) || !Half.IsFinite((Half)value)) throw new InvalidDataException("Nonfinite snow attribute.");
                BinaryPrimitives.WriteHalfBigEndian(to[toOffset..],(Half)value);
            }
            // Snow packs AO UVs into TEXCOORD0.zw; DiRT 2 reads TEXCOORD1.xy.
            var normal=new Vector3(BinaryPrimitives.ReadSingleBigEndian(from[16..]),BinaryPrimitives.ReadSingleBigEndian(from[20..]),BinaryPrimitives.ReadSingleBigEndian(from[24..]));
            var tangent=new Vector3(BinaryPrimitives.ReadSingleBigEndian(from[32..]),BinaryPrimitives.ReadSingleBigEndian(from[36..]),BinaryPrimitives.ReadSingleBigEndian(from[40..]));
            // Same handedness as DiRT 3's non-tessellated snow vertex shader.
            var binormal=Vector3.Cross(tangent,normal);
            if(!float.IsFinite(binormal.LengthSquared()) || binormal.LengthSquared()<.01f) throw new InvalidDataException("Invalid snow tangent basis.");
            for(int k=0;k<3;k++) BinaryPrimitives.WriteHalfBigEndian(to[(44+k*2)..],(Half)binormal[k]);
            foreach(int offset in new[]{0,4,8})
                if(!float.IsFinite(BinaryPrimitives.ReadSingleBigEndian(to[offset..]))) throw new InvalidDataException("Nonfinite snow vertex.");
        }
        var layout=new[]{("Vertex","float3",0),("Color","uint_color_argb",12),("ST","half4",16),("ST","half2",24),("Normal","half4",28),("Tangent","half4",36),("Binormal","half4",44)};
        block.Elements("DATABLOCKSTREAM").Remove();
        block.Element("DATABLOCKDATA")!.AddBeforeSelf(layout.Select(f=>new XElement("DATABLOCKSTREAM",new XAttribute("renderType",f.Item1),new XAttribute("dataType",f.Item2),new XAttribute("offset",f.Item3),new XAttribute("stride",52))));
        block.Element("DATABLOCKDATA")!.Value=EgoEngineLibrary.Helper.HexHelper.ByteArrayToHexViaLookup32(result);
        block.SetAttributeValue("size",result.Length); block.SetAttributeValue("streamCount",layout.Length);
    }

    internal static object Bake(string sourcePath,string scenePath,Dictionary<string,XElement> templates,List<object> shaderReport)
    {
        var snow=PortFiles.ReadPssg(sourcePath); var scene=PortFiles.ReadPssg(scenePath);
        foreach(var transform in snow.Descendants("TRANSFORM"))
            if(!Values(transform.Value).SequenceEqual(new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1})) throw new InvalidDataException("Nonidentity snow transform.");
        ShaderConversion.Convert(snow,templates,"route_0/snow.pssg (static terrain)",shaderReport);
        var road=templates["terrain_road.fx"].Elements("SHADERINPUTDEFINITION").ToArray();
        int Parameter(string name) => Array.FindIndex(road,d=>(string?)d.Attribute("name")==name);
        foreach(var material in snow.Descendants("SHADERINSTANCE"))
        {
            foreach(var pair in new[]{("TDiffuseSpecMap3","TDiffuseSpecMap1"),("TNormalMap3","TNormalMap1"),("TBlendMap3","TBlendMap2")})
            {
                var input=new XElement(material.Elements("SHADERINPUT").Single(i=>(int)i.Attribute("parameterID")! == Parameter(pair.Item2)));
                input.SetAttributeValue("parameterID",Parameter(pair.Item1)); material.Add(input);
            }
            foreach(var pair in new[]{("DetailBlendPower","4"),("DetailBlendScale","0.5")})
                material.Add(new XElement("SHADERINPUT",new XAttribute("parameterID",Parameter(pair.Item1)),new XAttribute("type","constant"),new XAttribute("format","float"),pair.Item2));
            if(material.Elements("SHADERINPUT").Any(i=>((string?)i.Attribute("texture"))?.StartsWith('#')==true)) throw new InvalidDataException("Unconverted local snow texture dependency.");
            // These materials are moving into tracksplit itself. An external
            // reference back to that loading file can deadlock DiRT 2's loader.
            foreach(var texture in material.Elements("SHADERINPUT").Attributes("texture"))
                if(texture.Value.StartsWith("tracksplit.pssg#",StringComparison.Ordinal)) texture.Value=texture.Value[15..];
            material.SetAttributeValue("parameterSavedCount",material.Elements("SHADERINPUT").Count());
        }
        var ids=snow.Descendants().Where(e=>e.Attribute("id") is not null && e.Name.LocalName!="SHADERGROUP").ToDictionary(e=>(string)e.Attribute("id")!,e=>"aspen_snow_"+(string)e.Attribute("id")!);
        if(scene.Descendants().Any(e=>e.Attribute("id") is {} id && ids.ContainsValue(id.Value))) throw new InvalidDataException("Static snow already present.");
        foreach(var attribute in snow.Descendants().Attributes())
            if(attribute.Name=="id" && ids.TryGetValue(attribute.Value,out var id)) attribute.Value=id;
            else if(attribute.Value.StartsWith('#') && ids.TryGetValue(attribute.Value[1..],out var reference)) attribute.Value="#"+reference;
        XElement Library(string type) => scene.Descendants("LIBRARY").Single(l=>(string?)l.Attribute("type")==type);
        foreach(var material in snow.Descendants("SHADERINSTANCE")) Library("SHADERINSTANCE").Add(new XElement(material));
        foreach(var block in snow.Descendants("DATABLOCK")) { ConvertVertices(block); Library("RENDERINTERFACEBOUND").Add(new XElement(block)); }
        var nodes=snow.Descendants().Where(e=>e.Attribute("id") is not null).ToDictionary(e=>(string)e.Attribute("id")!);
        var tiles=scene.Descendants("NODE").Where(n=>((string?)n.Attribute("id"))?.StartsWith("ROOT_",StringComparison.Ordinal)==true)
            .Select(n=>new { High=n.Elements("RENDERNODE").SingleOrDefault(r=>((string?)r.Attribute("id"))?.StartsWith("HIGH_",StringComparison.Ordinal)==true),
                Low=n.Elements("RENDERNODE").SingleOrDefault(r=>((string?)r.Attribute("id"))?.StartsWith("LOW_",StringComparison.Ordinal)==true) })
            .Where(n=>n.High is not null && n.Low is not null).Select(n=>(High:n.High!,Bounds:Values(n.Low!.Element("BOUNDINGBOX")!.Value))).ToArray();
        if(tiles.Length==0) throw new InvalidDataException("No existing snow terrain tiles.");
        float Distance(float[] b,Vector3 p) { float x=Math.Max(b[0]-p.X,Math.Max(0,p.X-b[3])),z=Math.Max(b[2]-p.Z,Math.Max(0,p.Z-b[5]));return x*x+z*z; }
        int draws=0,triangles=0; var coverage=new List<object>();
        foreach(var instance in snow.Descendants("RENDERSTREAMINSTANCE"))
        {
            var source=nodes[((string)instance.Element("RENDERINSTANCESOURCE")!.Attribute("source")!)[1..]];
            var blockIds=source.Elements("RENDERSTREAM").Select(s=>((string)s.Attribute("dataBlock")!)[1..]).Distinct().ToArray();
            if(blockIds.Length!=1 || (string?)source.Attribute("primitive")!="triangles") throw new InvalidDataException("Unsupported snow mesh.");
            var block=nodes[blockIds[0]]; var bytes=Bytes(block); int vertices=(int)block.Attribute("elementCount")!;
            var original=source.Element("RENDERINDEXSOURCE")!;
            var indices=original.Element("INDEXSOURCEDATA")!.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            if(indices.Length%3!=0 || indices.Length!=(int)original.Attribute("count")! || indices.Any(i=>i<0 || i>=vertices)) throw new InvalidDataException("Invalid snow triangle indices.");
            var groups=new Dictionary<XElement,List<int>>();
            for(int i=0;i<indices.Length;i+=3)
            {
                var centre=(Position(bytes,indices[i])+Position(bytes,indices[i+1])+Position(bytes,indices[i+2]))/3;
                var tile=tiles.OrderBy(t=>Distance(t.Bounds,centre)).ThenBy(t=>Vector2.DistanceSquared(new((t.Bounds[0]+t.Bounds[3])/2,(t.Bounds[2]+t.Bounds[5])/2),new(centre.X,centre.Z))).First().High;
                if(!groups.TryGetValue(tile,out var list)) groups.Add(tile,list=[]);
                list.AddRange(indices.AsSpan(i,3).ToArray());
            }
            if(groups.Values.Sum(g=>g.Count)!=indices.Length) throw new InvalidDataException("Snow triangle partition lost geometry.");
            foreach(var (tile,list) in groups)
            {
                string id=(string)source.Attribute("id")!+"_"+(string)tile.Attribute("id")!;
                var mesh=new XElement(source); mesh.SetAttributeValue("id",id); mesh.SetAttributeValue("streamCount",7);
                var index=mesh.Element("RENDERINDEXSOURCE")!; index.SetAttributeValue("id",id+"_indices"); index.SetAttributeValue("count",list.Count); index.SetAttributeValue("maximumIndex",list.Max()); index.Element("INDEXSOURCEDATA")!.Value=string.Join(' ',list);
                mesh.Elements("RENDERSTREAM").Remove();
                for(int i=0;i<7;i++) mesh.Add(new XElement("RENDERSTREAM",new XAttribute("id",id+"_stream_"+i),new XAttribute("dataBlock","#"+blockIds[0]),new XAttribute("subStream",i)));
                Library("SEGMENTSET").Add(new XElement("SEGMENTSET",new XAttribute("id",id+"_segment"),new XAttribute("segmentCount",1),mesh));
                var draw=new XElement(instance); draw.SetAttributeValue("id",id+"_draw"); draw.SetAttributeValue("indices","#"+id); draw.Element("RENDERINSTANCESOURCE")!.SetAttributeValue("source","#"+id);tile.Add(draw);
                var positions=list.Select(i=>Position(bytes,i)).ToArray();var min=positions.Aggregate(Vector3.Min);var max=positions.Aggregate(Vector3.Max);
                for(XElement? node=tile;node is not null;node=node.Parent)
                    if(node.Element("BOUNDINGBOX") is {} box) { var b=Values(box.Value);box.Value=Text(new[]{Math.Min(b[0],min.X),Math.Min(b[1],min.Y),Math.Min(b[2],min.Z),Math.Max(b[3],max.X),Math.Max(b[4],max.Y),Math.Max(b[5],max.Z)}); }
                draws++;
            }
            triangles+=indices.Length/3; coverage.Add(new { Source=(string)source.Attribute("id")!,Triangles=indices.Length/3,Tiles=groups.Keys.Select(t=>(string)t.Attribute("id")!).ToArray() });
        }
        PortFiles.WritePssg(scene,scenePath+".tmp"); ObjectVertexLayout.Verify(scene,PortFiles.ReadPssg(scenePath+".tmp"));File.Move(scenePath+".tmp",scenePath,true);
        return new { SourceMeshes=coverage.Count,Triangles=triangles,Draws=draws,PositionsAndWindingPreserved=true,DeformationSupported=false,Coverage=coverage,RuntimeValidated=false };
    }
}
