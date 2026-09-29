using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

// Laboratory fallback: put static prop meshes into Aspen's existing terrain
// tiles. Keep the entity collision definitions and dynamic ornament slots.
internal static class StaticProps
{
    // These mixed static/dynamic models render correctly through DiRT 2's
    // native batch. Clearing their static count also suppresses the movable
    // barrier row in the captured views, despite retaining its entity slots.
    // Keep this limited to the models verified in the laboratory: some other
    // DiRT 3 models still fail in DiRT 2's native batching path.
    static bool KeepNativeBatch(string name) => name is "core_barr_blockplastic2_c~0" or "core_barr_blockplastic2_c~1";
    static float[] Values(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    static string Text(IEnumerable<float> values) => string.Join(' ', values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    static Matrix4x4 Matrix(float[] f) => new(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]);
    internal static object Bake(string track)
    {
        var objects = PortFiles.ReadPssg(Path.Combine(track, "objects.pssg"));
        var path = Path.Combine(track, "tracksplit.pssg"); var scene = PortFiles.ReadPssg(path);
        var nodes = objects.Descendants().Where(e => e.Attribute("id") is not null).ToDictionary(e => (string)e.Attribute("id")!);
        XElement Library(string type) => scene.Descendants("LIBRARY").Single(e => (string?)e.Attribute("type") == type);
        foreach (var type in new[] { "SHADERGROUP", "SHADERINSTANCE" })
        foreach (var item in objects.Descendants(type))
        {
            var copy = new XElement(item);
            if (type == "SHADERGROUP")
            {
                var existing = Library(type).Elements(type).SingleOrDefault(e => (string?)e.Attribute("id") == (string?)item.Attribute("id"));
                if (existing is not null)
                {
                    if (!XNode.DeepEquals(existing, item)) throw new InvalidDataException("Conflicting scene shader definition.");
                    continue;
                }
            }
            else copy.SetAttributeValue("id", "aspen_static_" + (string)item.Attribute("id")!);
            Library(type).Add(copy);
        }
        var tiles = scene.Descendants("RENDERNODE").Where(e => ((string?)e.Attribute("id"))?.StartsWith("HIGH_", StringComparison.Ordinal) == true).ToArray();
        if (tiles.Length == 0) throw new InvalidDataException("No Aspen terrain tiles.");
        var tileBounds = tiles.ToDictionary(t => t, t => Values(t.Element("BOUNDINGBOX")!.Value));
        foreach (var tile in tiles)
            if (Matrix(Values(tile.Element("TRANSFORM")!.Value)) != Matrix4x4.Identity) throw new InvalidDataException("Nonidentity terrain tile transform.");
        float Distance(XElement tile, Vector3 position)
        {
            var b = tileBounds[tile];
            float x = Math.Max(b[0] - position.X, Math.Max(0, position.X - b[3]));
            float z = Math.Max(b[2] - position.Z, Math.Max(0, position.Z - b[5])); return x*x + z*z;
        }
        void Expand(XElement tile, Vector3 min, Vector3 max)
        {
            for (XElement? node = tile; node is not null; node = node.Parent)
                if (node.Element("BOUNDINGBOX") is { } box)
                {
                    var b = Values(box.Value); box.Value = Text(new[] { Math.Min(b[0],min.X),Math.Min(b[1],min.Y),Math.Min(b[2],min.Z),Math.Max(b[3],max.X),Math.Max(b[4],max.Y),Math.Max(b[5],max.Z) });
                }
        }
        var ornamentsPath = Path.Combine(track, "route_0", "ornaments.bin"); var ornaments = File.ReadAllBytes(ornamentsPath);
        int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(ornaments.AsSpan(at));
        string S(int at) => Encoding.ASCII.GetString(ornaments, at, Array.IndexOf(ornaments, (byte)0, at) - at);
        int references = I(40), table = I(36), placements = 0, meshes = 0, vertices = 0, triangles = 0;
        var evidence = new List<object>();
        var nativeBatches = new List<object>();
        for (int reference = 0; reference < references; reference++)
        {
            int at = table + reference * 56, start = I(at + 44), count = I(at + 48); string name = S(I(at));
            if (count == 0) continue;
            if (KeepNativeBatch(name))
            {
                nativeBatches.Add(new { Model=name, StaticPlacements=count, Capacity=I(at+36) });
                continue; // Preserve all placements/flags/slots; do not also bake a copy.
            }
            var root = nodes[name + " Root"];
            var lod = root.Descendants("LODVISIBLERENDERNODE").Single();
            // The direct LOD meshes already contain the model-space pivot.
            // The LOD node's transform describes that pivot, not an extra mesh
            // transform. Applying it again lifts plastic barriers by 0.652 m.
            var modelTransform = Matrix(Values(root.Element("TRANSFORM")!.Value));
            for (int placement = 0; placement < count; placement++)
            {
                int offset = start + placement * 64; var f = new float[16]; f[15] = 1;
                int[] packed = [0,1,2,4,5,6,8,9,10,12,13,14];
                for (int i = 0; i < 12; i++) f[packed[i]] = BinaryPrimitives.ReadSingleLittleEndian(ornaments.AsSpan(offset + i * 4));
                var world = modelTransform * Matrix(f);
                if (world.GetDeterminant() <= 0 || !Matrix4x4.Invert(world, out var inverse)) throw new InvalidDataException("Reflected or singular static prop transform.");
                var normals = Matrix4x4.Transpose(inverse);
                var tile = tiles.MinBy(t => Distance(t, world.Translation))!;
                var low = scene.Descendants("RENDERNODE").Single(e => (string?)e.Attribute("id") == ((string)tile.Attribute("id")!).Replace("HIGH_", "LOW_"));
                foreach (var instance in lod.Elements("RENDERSTREAMINSTANCE"))
                {
                    var source = nodes[((string)instance.Elements("RENDERINSTANCESOURCE").Single().Attribute("source")!)[1..]];
                    string id = "aspen_static_RDS_" + meshes++, shader = "#aspen_static_" + ((string)instance.Attribute("shader")!)[1..];
                    var mesh = new XElement(source); mesh.SetAttributeValue("id", id);
                    var indices = mesh.Element("RENDERINDEXSOURCE")!; indices.SetAttributeValue("id", id + "_indices");
                    triangles += (int)indices.Attribute("count")! / 3;
                    var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
                    var blocks = new Dictionary<string, string>();
                    foreach (var stream in mesh.Elements("RENDERSTREAM"))
                    {
                        string blockId = ((string)stream.Attribute("dataBlock")!)[1..];
                        if (!blocks.TryGetValue(blockId, out var newId))
                        {
                            newId = id + "_block_" + blocks.Count; blocks.Add(blockId, newId);
                            var block = new XElement(nodes[blockId]); block.SetAttributeValue("id", newId);
                            var data = block.Element("DATABLOCKDATA")!;
                            var bytes = Convert.FromHexString(string.Concat(data.Value.Where(c => !char.IsWhiteSpace(c))));
                            foreach (var field in block.Elements("DATABLOCKSTREAM"))
                            {
                                string semantic = (string)field.Attribute("renderType")!, format = (string)field.Attribute("dataType")!;
                                if (semantic is not ("Vertex" or "Normal" or "Tangent" or "Binormal")) continue;
                                if (format is not ("float3" or "half4")) throw new InvalidDataException("Unsupported static prop vector format.");
                                int stride = (int)field.Attribute("stride")!, fieldOffset = (int)field.Attribute("offset")!, n = (int)block.Attribute("elementCount")!;
                                for (int vertex = 0; vertex < n; vertex++)
                                {
                                    int p = fieldOffset + vertex * stride;
                                    float Read(int c) => format == "float3" ? BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(p+c*4)) : (float)BinaryPrimitives.ReadHalfBigEndian(bytes.AsSpan(p+c*2));
                                    var value = new Vector3(Read(0),Read(1),Read(2));
                                    value = semantic == "Vertex" ? Vector3.Transform(value, world) : Vector3.Normalize(Vector3.TransformNormal(value, normals));
                                    if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) throw new InvalidDataException("Nonfinite baked prop vector.");
                                    if (semantic == "Vertex") { min=Vector3.Min(min,value); max=Vector3.Max(max,value); vertices++; }
                                    for (int c = 0; c < 3; c++)
                                        if (format == "float3") BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(p+c*4),value[c]);
                                        else BinaryPrimitives.WriteHalfBigEndian(bytes.AsSpan(p+c*2),(Half)value[c]);
                                }
                            }
                            data.Value = EgoEngineLibrary.Helper.HexHelper.ByteArrayToHexViaLookup32(bytes); Library("RENDERINTERFACEBOUND").Add(block);
                        }
                        stream.SetAttributeValue("dataBlock", "#" + newId); stream.SetAttributeValue("id", id + "_stream_" + (string)stream.Attribute("id")!);
                    }
                    if (min.X == float.MaxValue) throw new InvalidDataException("Missing static prop positions.");
                    Library("SEGMENTSET").Add(new XElement("SEGMENTSET",new XAttribute("id",id+"_segment"),new XAttribute("segmentCount",1),mesh));
                    foreach (var target in new[] { tile, low })
                    {
                        target.Add(new XElement("RENDERSTREAMINSTANCE",new XAttribute("id",id+"_"+(string)target.Attribute("id")!),new XAttribute("sourceCount",1),new XAttribute("streamCount",0),new XAttribute("indices","#"+id),new XAttribute("shader",shader),new XElement("RENDERINSTANCESOURCE",new XAttribute("source","#"+id))));
                        Expand(target,min,max);
                    }
                }
                placements++;
            }
            evidence.Add(new { Model=name, Placements=count, LodPivotApplied=false });
            BinaryPrimitives.WriteInt32LittleEndian(ornaments.AsSpan(at+48),0);
        }
        PortFiles.WritePssg(scene,path+".tmp"); ObjectVertexLayout.Verify(scene,PortFiles.ReadPssg(path+".tmp")); File.Move(path+".tmp",path,true);
        File.WriteAllBytes(ornamentsPath,ornaments);
        return new { Placements=placements, Meshes=meshes, Vertices=vertices, Triangles=triangles, Models=evidence, NativeBatches=nativeBatches, RuntimeValidated=false, HighestLodOnly=true };
    }
}
