using System.Buffers.Binary;
using System.Numerics;
using System.Xml.Linq;
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
    internal const string SpeedNode = "int_f04_speed";
    internal const string GearNode = "int_f04_gear";
    internal const string RpmFactor = "9.549296596";
    internal static XElement Configuration() => new("displays", new XAttribute("type", "digital"),
        new XElement("display", new XAttribute("value", "tach"), new XAttribute("node", RpmNode), new XAttribute("font", "int_digi_speed"), new XAttribute("num_chars", "5"), new XAttribute("factor", RpmFactor)),
        new XElement("display", new XAttribute("value", "speed"), new XAttribute("node", SpeedNode), new XAttribute("font", "int_digi_speed"), new XAttribute("num_chars", "3"), new XAttribute("factor", "3.6")),
        // Native gear values select the N/R glyphs in this stock font atlas.
        new XElement("display", new XAttribute("value", "gear"), new XAttribute("node", GearNode), new XAttribute("font", "int_digi_gear"), new XAttribute("num_chars", "1")));

    internal static void Add(PssgFile cockpit, string donor)
    {
        using var input = File.OpenRead(Path.Combine(donor, "interiors/prc/int_prc.pssg"));
        var native = PssgFile.Open(input);
        var template = native.Elements<PssgRenderNode>().Single(n => n.Id == "int_digi_speed");
        var nativeSource = template.RenderInstances.Single().GetRenderDataSource();
        var nativeBlock = nativeSource.Streams.First().GetDataBlock();
        if (nativeBlock.ElementCount != 6 || nativeBlock.Streams.Single(s => s.RenderType == "Vertex").Stride != 32)
            throw new InvalidDataException("Unsupported native digital-display geometry.");
        var backingTemplate = native.Elements<PssgRenderNode>().Single(n => n.Id == "int_indicator_gear");
        var backingSource = backingTemplate.RenderInstances.Single().GetRenderDataSource();
        var backingBlock = backingSource.Streams.First().GetDataBlock();
        if (backingBlock.ElementCount != 4 || backingBlock.Streams.Single(s => s.RenderType == "Vertex").Stride != 56)
            throw new InvalidDataException("Unsupported native matte-display geometry.");
        var steering = cockpit.Elements<PssgNode>().Single(n => n.Id == "steering_wheel");
        var materials = cockpit.Elements<PssgLibrary>().Single(l => l.Type == "SHADERINSTANCE");
        var resources = cockpit.Elements<PssgLibrary>().Single(l => l.Type == "RENDERINTERFACEBOUND");
        var segments = cockpit.Elements<PssgLibrary>().Single(l => l.Type == "SEGMENTSET");
        // Each controller rewrites its material's font map/UV parameters. Sharing
        // an instance would make RPM, speed and gear overwrite each other's text.
        // Separate IDs alone are insufficient: equivalent native material inputs
        // can resolve to the same runtime material. Give each an owned texture too.
        foreach (string name in new[] { "rpm", "speed", "gear" })
        {
            var shader = Clone(template.RenderInstances.Single().GetShaderInstance());
            var placeholder = Clone(native.Elements<PssgTexture>().Single(t => t.Id == "int_digi_speed.tga"));
            placeholder.Id = "f04_" + name + "_font_surface"; resources.AppendChild(placeholder);
            var group = new XElement("root"); shader.GetShaderGroup().WriteXml(group);
            var fields = group.Elements().Single().Elements("SHADERINPUTDEFINITION").Select(d => (string)d.Attribute("name")!).ToArray();
            shader.Inputs.Single(i => i.ParameterId == Array.IndexOf(fields, "TEmissiveMap")).Texture = "#" + placeholder.Id;
            // The donor cabin cubemap contains its dashboard/window details.
            // Those reflections read as ghosted instruments over the F1 digits.
            foreach (string field in new[] { "EnvironmentColour", "PhongColour" })
                shader.Inputs.Single(i => i.ParameterId == Array.IndexOf(fields, field)).DisplayValue = "0 0 0";
            foreach (string field in new[] { "FresnelMin", "FresnelMax" })
                shader.Inputs.Single(i => i.ParameterId == Array.IndexOf(fields, field)).DisplayValue = "0";
            shader.Id = "f04_" + name + "_digits"; materials.AppendChild(shader);
        }

        // A labelled LCD backing, independent of AC's unpopulated UI atlas.
        // The native speed controller applies the game's selected display units.
        const int size = 256;
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
        {
            pixels[i * 4] = 190; pixels[i * 4 + 1] = 200; pixels[i * 4 + 2] = 190; pixels[i * 4 + 3] = 255;
        }
        var glyphs = new Dictionary<char, string[]>
        {
            ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
            ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
            ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
            ['G'] = ["01111", "10000", "10000", "10111", "10001", "10001", "01111"],
            ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
            ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
            ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
            ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"]
        };
        void Label(string text, int x, int y)
        {
            foreach (char character in text)
            {
                var glyph = glyphs[character];
                for (int row = 0; row < 7; row++) for (int col = 0; col < 5; col++) if (glyph[row][col] == '1')
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                    {
                        int offset = ((y + row * 2 + dy) * size + x + col * 2 + dx) * 4;
                        pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 30;
                    }
                x += 12;
            }
        }
        Label("RPM", 110, 9); Label("GEAR", 18, 120); Label("SPEED", 147, 120);
        var encoder = new BcEncoder(CompressionFormat.Bc1);
        encoder.OutputOptions.GenerateMipMaps = true;
        using var encoded = new MemoryStream();
        encoder.EncodeToDds(pixels, size, size, PixelFormat.Bgra32).Write(encoded); encoded.Position = 0;
        var texture = (PssgTexture)cockpit.Elements<PssgTexture>().First().DeepClone();
        texture.Id = "f04_rpm_backing"; new DdsFile(encoded).ToPssgElement(texture); texture.AutoMipMap = false;
        resources.AppendChild(texture);
        // Preserve the accepted opaque matte backing, using the donor's complete
        // interior.fx vertex layout so the labelled diffuse map receives its UVs.
        var backingShader = Clone(cockpit.Elements<PssgShaderInstance>().Single(s => s.Id == "f04_int_material_1"));
        backingShader.Id = "f04_rpm_backing_material";
        var definitions = new XElement("root"); backingShader.GetShaderGroup().WriteXml(definitions);
        var names = definitions.Elements().Single().Elements("SHADERINPUTDEFINITION").Select(d => (string)d.Attribute("name")!).ToArray();
        int diffuse = Array.IndexOf(names, "TDiffuseAlphaMap");
        backingShader.Inputs.Single(i => i.ParameterId == diffuse).Texture = "#" + texture.Id;
        // Native interior.fx still samples the donor maps with their scales at
        // zero. Flat maps remove its embossed PRC dashboard from the LCD itself.
        // RGB normals match DiRT's interior format; original car maps stay intact.
        foreach (var (field, id, red, green, blue) in new (string, string, byte, byte, byte)[]
        {
            ("TNormalMap", "f04_lcd_flat_normal", 128, 128, 255),
            ("TSpecularMap", "f04_lcd_black_specular", 0, 0, 0),
            ("TOcclusionMap", "f04_lcd_white_occlusion", 255, 255, 255)
        })
        {
            var solid = new byte[4 * 4 * 4];
            for (int i = 0; i < 16; i++)
            {
                solid[i * 4] = blue; solid[i * 4 + 1] = green;
                solid[i * 4 + 2] = red; solid[i * 4 + 3] = 255;
            }
            var flatEncoder = new BcEncoder(CompressionFormat.Bgra);
            flatEncoder.OutputOptions.GenerateMipMaps = true;
            using var flatDds = new MemoryStream();
            flatEncoder.EncodeToDds(solid, 4, 4, PixelFormat.Bgra32).Write(flatDds); flatDds.Position = 0;
            var flatTexture = Clone(texture); flatTexture.Id = id;
            new DdsFile(flatDds).ToPssgElement(flatTexture); flatTexture.AutoMipMap = false;
            resources.AppendChild(flatTexture);
            backingShader.Inputs.Single(i => i.ParameterId == Array.IndexOf(names, field)).Texture = "#" + id;
        }
        foreach (string name in new[] { "EnvironmentColour", "PhongColour" })
        {
            int id = Array.IndexOf(names, name);
            if (id >= 0 && backingShader.Inputs.SingleOrDefault(i => i.ParameterId == id) is { } parameter) parameter.DisplayValue = "0 0 0";
        }
        foreach (string name in new[] { "NormalMapScale", "OcclusionMapScale", "EmissiveMapScale", "FresnelMin", "FresnelMax" })
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
        void Quad(string id, float left, float right, float bottom, float top, float z, string shader, bool backing = false)
        {
            if (cockpit.TryGetObject<PssgNode>(id.AsMemory()) is not null) throw new InvalidDataException("Duplicate dashboard node.");
            var primitive = backing ? backingTemplate : template;
            var primitiveSource = backing ? backingSource : nativeSource;
            var primitiveBlock = backing ? backingBlock : nativeBlock;
            var node = Clone(primitive); node.Id = node.Nickname = id;
            node.Transform.Transform = Matrix4x4.Identity;
            node.BoundingBox.BoundsMin = new(left, bottom, z - .0005f);
            node.BoundingBox.BoundsMax = new(right, top, z + .0005f);
            var block = Clone(primitiveBlock); block.Id = id + "_vertices";
            Vector3[] positions = backing
                ? [new(left, top, z), new(right, top, z), new(right, bottom, z), new(left, bottom, z)]
                : [new(left, top, z), new(right, top, z), new(right, bottom, z), new(right, bottom, z), new(left, bottom, z), new(left, top, z)];
            Vector2[] uv = [new(1, 0), new(0, 0), new(0, 1), new(1, 1)];
            int stride = backing ? 56 : 32;
            for (int i = 0; i < positions.Length; i++)
            {
                int offset = i * stride;
                foreach (float value in new[] { positions[i].X, positions[i].Y, positions[i].Z, 0f, 0f, -1f })
                {
                    BinaryPrimitives.WriteInt32BigEndian(block.Data.Value.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value)); offset += 4;
                }
                if (backing)
                {
                    foreach (float value in new[] { -1f, 0f, 0f, 0f, 1f, 0f })
                    {
                        BinaryPrimitives.WriteInt32BigEndian(block.Data.Value.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value)); offset += 4;
                    }
                    foreach (float value in new[] { uv[i].X, uv[i].Y })
                    {
                        BinaryPrimitives.WriteInt32BigEndian(block.Data.Value.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value)); offset += 4;
                    }
                }
            }
            var source = Clone(primitiveSource); source.Id = id + "_source";
            source.IndexSource!.Id = id + "_indices";
            int streamIndex = 0;
            foreach (var stream in source.Streams) { stream.Id = id + "_stream_" + streamIndex++; stream.DataBlock = "#" + block.Id; }
            var set = Clone((PssgSegmentSet)primitiveSource.ParentElement!); set.Id = id + "_segments";
            foreach (var child in set.Segments.ToArray()) set.RemoveChild(child);
            set.AppendChild(source); set.SegmentCount = 1;
            resources.AppendChild(block); segments.AppendChild(set);
            var instance = node.RenderInstances.Single(); instance.Id = id + "_instance";
            instance.Indices = instance.RenderInstanceSource.Source = "#" + source.Id; instance.Shader = "#" + shader;
            steering.AppendChild(node);
        }
        // In front of the source LCD cover; source buttons and glass are preserved.
        Quad("int_f04_rpm_backplate", -.040f, .040f, .035f, .092f, -.074f, backingShader.Id, true);
        Quad(RpmNode, -.0365f, .0365f, .067f, .084f, -.075f, "f04_rpm_digits");
        Quad(GearNode, .018f, .034f, .042f, .060f, -.075f, "f04_gear_digits");
        Quad(SpeedNode, -.034f, .008f, .042f, .060f, -.075f, "f04_speed_digits");
    }
}
