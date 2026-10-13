using System.Buffers.Binary;
using System.Numerics;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

namespace DiRT2VR.CustomCars;

// DiRT's digital-display controller replaces the native material's font maps.
// Its dark glyphs need a light backing, independent of the AC display atlas.
internal static class FerrariDashboard
{
    internal const string RpmNode = "int_f04_rpm";
    internal const string RpmFactor = "9.549296596";
    internal static void Add(PssgFile cockpit, string donor)
    {
        using var input = File.OpenRead(Path.Combine(donor, "interiors/prc/int_prc.pssg"));
        var native = PssgFile.Open(input);
        var template = native.Elements<PssgRenderNode>().Single(n => n.Id == "int_digi_speed");
        var nativeSource = template.RenderInstances.Single().GetRenderDataSource();
        var nativeBlock = nativeSource.Streams.First().GetDataBlock();
        if (nativeBlock.ElementCount != 6 || nativeBlock.Streams.Single(s => s.RenderType == "Vertex").Stride != 32)
            throw new InvalidDataException("Unsupported native digital-display geometry.");
        var steering = cockpit.Elements<PssgNode>().Single(n => n.Id == "steering_wheel");
        var materials = cockpit.Elements<PssgLibrary>().Single(l => l.Type == "SHADERINSTANCE");
        var resources = cockpit.Elements<PssgLibrary>().Single(l => l.Type == "RENDERINTERFACEBOUND");
        var segments = cockpit.Elements<PssgLibrary>().Single(l => l.Type == "SEGMENTSET");
        var rpmShader = (PssgShaderInstance)template.RenderInstances.Single().GetShaderInstance().DeepClone();
        rpmShader.Id = "f04_rpm_digits"; materials.AppendChild(rpmShader);

        // A small constant-colour material, rather than AC's unpopulated UI atlas.
        var pixels = new byte[8 * 8 * 4];
        for (int i = 0; i < 64; i++)
        {
            pixels[i * 4] = 190; pixels[i * 4 + 1] = 200; pixels[i * 4 + 2] = 190; pixels[i * 4 + 3] = 255;
        }
        var encoder = new BcEncoder(CompressionFormat.Bc1);
        encoder.OutputOptions.GenerateMipMaps = false;
        using var encoded = new MemoryStream();
        encoder.EncodeToDds(pixels, 8, 8, PixelFormat.Bgra32).Write(encoded); encoded.Position = 0;
        var texture = (PssgTexture)cockpit.Elements<PssgTexture>().First().DeepClone();
        texture.Id = "f04_rpm_backing"; new DdsFile(encoded).ToPssgElement(texture); texture.AutoMipMap = false;
        resources.AppendChild(texture);
        var backingShader = (PssgShaderInstance)cockpit.Elements<PssgShaderInstance>().Single(s => s.Id == "f04_int_material_1").DeepClone();
        backingShader.Id = "f04_rpm_backing_material";
        var definitions = new System.Xml.Linq.XElement("root"); backingShader.GetShaderGroup().WriteXml(definitions);
        var names = definitions.Elements().Single().Elements("SHADERINPUTDEFINITION").Select(d => (string)d.Attribute("name")!).ToArray();
        int diffuse = Array.IndexOf(names, "TDiffuseAlphaMap");
        backingShader.Inputs.Single(i => i.ParameterId == diffuse).Texture = "#" + texture.Id;
        foreach (string name in new[] { "NormalMapScale", "OcclusionMapScale" })
        {
            int id = Array.IndexOf(names, name);
            if (id >= 0 && backingShader.Inputs.SingleOrDefault(i => i.ParameterId == id) is { } parameter) parameter.DisplayValue = "0";
        }
        materials.AppendChild(backingShader);

        // EGO DeepClone retains data-array references. Each edited primitive must
        // own its transform, bounds and vertex bytes before another is generated.
        T Clone<T>(T original) where T : PssgElement
        {
            var copy = (T)original.DeepClone();
            void Detach(PssgElement element)
            {
                if (element.IsDataElement) element.Value = element.Value.ToArray();
                foreach (var child in element.ChildElements) Detach(child);
            }
            Detach(copy); return copy;
        }
        void Quad(string id, float halfWidth, float bottom, float top, float z, string shader)
        {
            if (cockpit.TryGetObject<PssgNode>(id.AsMemory()) is not null) throw new InvalidDataException("Duplicate dashboard node.");
            var node = Clone(template); node.Id = node.Nickname = id;
            node.Transform.Transform = Matrix4x4.Identity;
            node.BoundingBox.BoundsMin = new(-halfWidth, bottom, z - .0005f);
            node.BoundingBox.BoundsMax = new(halfWidth, top, z + .0005f);
            var block = Clone(nativeBlock); block.Id = id + "_vertices";
            Vector3[] positions = { new(-halfWidth, top, z), new(halfWidth, top, z), new(halfWidth, bottom, z),
                new(halfWidth, bottom, z), new(-halfWidth, bottom, z), new(-halfWidth, top, z) };
            for (int i = 0; i < 6; i++)
            {
                int offset = i * 32;
                foreach (float value in new[] { positions[i].X, positions[i].Y, positions[i].Z, 0f, 0f, -1f })
                {
                    BinaryPrimitives.WriteInt32BigEndian(block.Data.Value.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value)); offset += 4;
                }
            }
            var source = Clone(nativeSource); source.Id = id + "_source";
            source.IndexSource!.Id = id + "_indices";
            int streamIndex = 0;
            foreach (var stream in source.Streams) { stream.Id = id + "_stream_" + streamIndex++; stream.DataBlock = "#" + block.Id; }
            var set = Clone((PssgSegmentSet)nativeSource.ParentElement!); set.Id = id + "_segments";
            foreach (var child in set.Segments.ToArray()) set.RemoveChild(child);
            set.AppendChild(source); set.SegmentCount = 1;
            resources.AppendChild(block); segments.AppendChild(set);
            var instance = node.RenderInstances.Single(); instance.Id = id + "_instance";
            instance.Indices = instance.RenderInstanceSource.Source = "#" + source.Id; instance.Shader = "#" + shader;
            steering.AppendChild(node);
        }
        // In front of the source LCD cover; source buttons and glass are preserved.
        Quad("int_f04_rpm_backplate", .040f, .035f, .092f, -.074f, backingShader.Id);
        Quad(RpmNode, .0365f, .056f, .076f, -.075f, rpmShader.Id);
    }
}
