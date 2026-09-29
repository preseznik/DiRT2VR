using System.Buffers.Binary;
using System.Xml.Linq;

// Diagnostic layout normalization. It did NOT resolve the ornament batching
// crash and is not part of the normal converter. Verify also checks baked meshes.
internal static class ObjectVertexLayout
{
    internal static object Convert(XDocument document, bool keepColour = true)
    {
        var remaps = new Dictionary<string, int[]>();
        int vertices = 0;
        foreach (var block in document.Descendants("DATABLOCK"))
        {
            var streams = block.Elements("DATABLOCKSTREAM").ToArray();
            if (!streams.Any(s => (string?)s.Attribute("renderType") == "Vertex")) continue;
            var ordered = streams.Select((s, i) => (Stream: s, OldIndex: i)).Where(s => keepColour || (string?)s.Stream.Attribute("renderType") != "Color").OrderBy(s => Rank((string)s.Stream.Attribute("renderType")!)).ToArray();
            var data = block.Element("DATABLOCKDATA") ?? throw new InvalidDataException("Missing object vertex data.");
            var bytes = System.Convert.FromHexString(string.Concat(data.Value.Where(c => !char.IsWhiteSpace(c))));
            int count = (int)block.Attribute("elementCount")!;
            var sizes = ordered.Select(s => Size((string)s.Stream.Attribute("dataType")!, (string)s.Stream.Attribute("renderType")!)).ToArray();
            int stride = sizes.Sum(), offset = 0;
            var converted = new byte[checked(count * stride)];
            int[] map = Enumerable.Repeat(-1, streams.Length).ToArray();
            for (int index = 0; index < ordered.Length; index++)
            {
                var (stream, oldIndex) = ordered[index]; map[oldIndex] = index;
                int oldOffset = (int)stream.Attribute("offset")!, oldStride = (int)stream.Attribute("stride")!;
                string type = (string)stream.Attribute("dataType")!, semantic = (string)stream.Attribute("renderType")!;
                bool expand = type == "half4" && semantic is "Normal" or "Tangent" or "Binormal";
                for (int vertex = 0; vertex < count; vertex++)
                {
                    var from = bytes.AsSpan(checked(vertex * oldStride + oldOffset), expand ? 8 : sizes[index]);
                    var to = converted.AsSpan(vertex * stride + offset, sizes[index]);
                    if (expand)
                        for (int component = 0; component < 3; component++)
                        {
                            float value = (float)BinaryPrimitives.ReadHalfBigEndian(from[(component * 2)..]);
                            if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite object direction.");
                            BinaryPrimitives.WriteSingleBigEndian(to[(component * 4)..], value);
                        }
                    else from.CopyTo(to); // Positions, UVs and colours retain their exact bytes.
                }
                stream.SetAttributeValue("offset", offset); stream.SetAttributeValue("stride", stride);
                if (expand) stream.SetAttributeValue("dataType", "float3");
                offset += sizes[index];
            }
            foreach (var stream in streams) stream.Remove();
            data.AddBeforeSelf(ordered.Select(s => s.Stream));
            data.Value = EgoEngineLibrary.Helper.HexHelper.ByteArrayToHexViaLookup32(converted);
            block.SetAttributeValue("size", converted.Length);
            block.SetAttributeValue("streamCount", ordered.Length);
            remaps.Add("#" + (string)block.Attribute("id")!, map); vertices += count;
        }
        foreach (var source in document.Descendants("RENDERDATASOURCE"))
        {
            var streams = source.Elements("RENDERSTREAM").ToArray();
            if (!streams.Any(s => remaps.ContainsKey((string)s.Attribute("dataBlock")!))) continue;
            if (streams.Select(s => (string)s.Attribute("dataBlock")!).Distinct().Count() != 1)
                throw new InvalidDataException("Unexpected mixed static object data blocks.");
            foreach (var stream in streams)
            {
                var map = remaps[(string)stream.Attribute("dataBlock")!];
                stream.SetAttributeValue("subStream", map[(int)stream.Attribute("subStream")!]);
                stream.Remove();
            }
            source.Add(streams.Where(s => (int)s.Attribute("subStream")! >= 0).OrderBy(s => (int)s.Attribute("subStream")!));
            source.SetAttributeValue("streamCount", source.Elements("RENDERSTREAM").Count());
        }
        return new { Blocks = remaps.Count, Vertices = vertices, PositionsUvsPreserved = true, ColoursPreserved = keepColour };
    }

    internal static void Verify(XDocument planned, XDocument saved)
    {
        var actual = saved.Descendants("DATABLOCK").ToDictionary(e => (string)e.Attribute("id")!);
        if (actual.Count != planned.Descendants("DATABLOCK").Count()) throw new InvalidDataException("Mesh data block inventory changed.");
        foreach (var block in planned.Descendants("DATABLOCK"))
        {
            var other = actual[(string)block.Attribute("id")!];
            byte[] Bytes(XElement element) => System.Convert.FromHexString(string.Concat(element.Element("DATABLOCKDATA")!.Value.Where(c => !char.IsWhiteSpace(c))));
            var bytes = Bytes(other);
            if (bytes.Length != (int)other.Attribute("size")! || !bytes.AsSpan().SequenceEqual(Bytes(block)) ||
                !block.Elements("DATABLOCKSTREAM").Select(e => e.ToString()).SequenceEqual(other.Elements("DATABLOCKSTREAM").Select(e => e.ToString())))
                throw new InvalidDataException("Object vertex layout changed during binary serialization.");
        }
        var sources = saved.Descendants("RENDERDATASOURCE").ToDictionary(e => (string)e.Attribute("id")!);
        if (sources.Count != planned.Descendants("RENDERDATASOURCE").Count()) throw new InvalidDataException("Mesh source inventory changed.");
        foreach (var source in planned.Descendants("RENDERDATASOURCE"))
        {
            XElement Normalize(XElement element)
            {
                var copy = new XElement(element);
                foreach (var data in copy.Descendants("INDEXSOURCEDATA")) data.Value = string.Join(' ',data.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
                return copy;
            }
            if (!XNode.DeepEquals(Normalize(source),Normalize(sources[(string)source.Attribute("id")!])))
                throw new InvalidDataException("Mesh indices or stream references changed during binary serialization.");
        }
    }

    static int Rank(string semantic) => semantic switch
    {
        "Vertex" => 0, "ST" => 1, "Normal" => 2, "Tangent" => 3, "Binormal" => 4, "Color" => 5,
        _ => throw new InvalidDataException("Unsupported static object semantic: " + semantic)
    };
    static int Size(string type, string semantic) => type switch
    {
        "half4" when semantic is "Normal" or "Tangent" or "Binormal" => 12,
        "float3" => 12, "half2" or "uint_color_argb" => 4,
        _ => throw new InvalidDataException("Unsupported static object component: " + type)
    };
}
