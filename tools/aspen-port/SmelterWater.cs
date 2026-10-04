using System.Buffers.Binary;
using System.Xml.Linq;

// DiRT 3's shore-water type, constants and half-precision directions are not
// compatible with DiRT 2. Keep the authored patch geometry and adjacency.
internal static class SmelterWater
{
    internal const string Type = "DiRT2VR_Smelter_Water";
    internal static object Build(string track, string d2, string output, Dictionary<string, string> inputs)
    {
        var source = PortFiles.ReadXml(Path.Combine(track, "waterdefs_day.xml")).Root!.Elements("waterDef").Single();
        var definitionPath = Path.Combine(d2, "tracks/waterdefs.xml");
        inputs[definitionPath] = PortFiles.Hash(definitionPath);
        var definitions = PortFiles.ReadXml(definitionPath);
        var water = new XElement(definitions.Root!.Elements("waterDef").Single(n => (string?)n.Attribute("type") == "Water"));
        if (definitions.Root.Elements("waterDef").Any(n => (string?)n.Attribute("type") == Type))
            throw new InvalidDataException("Smelter water is already registered in the source installation.");
        // Only fields understood by DiRT 2; retain its defaults for fields that
        // DiRT 3 removed. Do not change any existing native water definition.
        foreach (var attribute in water.Attributes().Where(a => a.Name != "type").ToArray())
            if (source.Attribute(attribute.Name) is { } value) attribute.Value = value.Value;
        water.SetAttributeValue("type", Type);
        water.SetAttributeValue("i_water_draw_distance", "2000.0");
        definitions.Root.Add(water);
        // Water configuration uses text XML in DiRT 2, unlike surface_materials.
        // Do not use PortFiles.WriteXml's material-specific BXML default here.
        WriteSettings(definitions, Path.Combine(output, "waterdefs.xml"));

        var reports = new List<object>();
        foreach (var name in new[] { "iwater", "niwater" })
        {
            var path = Path.Combine(track, "route_0", name + ".pssg");
            var doc = PortFiles.ReadPssg(path);
            var donorPath = Path.Combine(d2, "tracks/baja/baja_iron/route_0", name + ".pssg");
            inputs[donorPath] = PortFiles.Hash(donorPath);
            var donor = PortFiles.ReadPssg(donorPath);
            var shader = name == "iwater" ? "#interactive_water.fx" : "#water.fx";
            var donorMaterial = donor.Descendants("SHADERINSTANCE").First(n => (string?)n.Attribute("shaderGroup") == shader);
            var donorFields = donor.Descendants("SHADERGROUP").Single(n => "#" + (string?)n.Attribute("id") == shader).Elements("SHADERINPUTDEFINITION").ToArray();
            var fields = doc.Descendants("SHADERGROUP").Single(n => "#" + (string?)n.Attribute("id") == shader).Elements("SHADERINPUTDEFINITION").ToArray();
            int Index(string field) => Array.FindIndex(fields, n => (string?)n.Attribute("name") == field);
            string Colour(string field) => string.Join(' ', ((string)source.Attribute(field)!).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(3));
            int materials = 0;
            foreach (var material in doc.Descendants("SHADERINSTANCE"))
            {
                if ((string?)material.Attribute("shaderGroup") != shader) throw new InvalidDataException("Unexpected Smelter water shader.");
                foreach (var input in donorMaterial.Elements("SHADERINPUT"))
                {
                    string field = (string)donorFields[(int)input.Attribute("parameterID")!].Attribute("name")!;
                    if (field == "TNormalMap") continue; // Retain Smelter's normal texture.
                    var copy = new XElement(input); int index = Index(field);
                    if (index < 0) throw new InvalidDataException("Missing native water field: " + field);
                    copy.SetAttributeValue("parameterID", index);
                    if (field == "TFoamTexture") copy.SetAttributeValue("texture", "#d2vr_smelter_foam");
                    if (field == "deepColour") copy.Value = Colour("i_water_colour_nadir_deep");
                    if (field == "shallowColour") copy.Value = Colour("i_water_colour_nadir");
                    if (field == "reflectionColour") copy.Value = Colour("i_water_colour_horizon");
                    material.Elements("SHADERINPUT").Where(n => (int)n.Attribute("parameterID")! == index).Remove();
                    material.Add(copy);
                }
                material.SetAttributeValue("parameterSavedCount", material.Elements("SHADERINPUT").Count());
                materials++;
            }
            if (name == "iwater")
            {
                var foam = donorMaterial.Elements("SHADERINPUT").Single(n => (string?)donorFields[(int)n.Attribute("parameterID")!].Attribute("name") == "TFoamTexture");
                string id = ((string)foam.Attribute("texture")!)[1..];
                var texture = new XElement(donor.Descendants("TEXTURE").Single(n => (string?)n.Attribute("id") == id));
                texture.SetAttributeValue("id", "d2vr_smelter_foam");
                var library = doc.Descendants("LIBRARY").SingleOrDefault(n => (string?)n.Attribute("type") == "TEXTURE");
                if (library is null) { library = new XElement("LIBRARY", new XAttribute("type", "TEXTURE")); doc.Root!.Element("PSSGDATABASE")!.Add(library); }
                library.Add(texture);
            }
            int vertices = ExpandDirections(doc, name == "iwater");
            PortFiles.WritePssg(doc, path + ".tmp");
            ObjectVertexLayout.Verify(doc, PortFiles.ReadPssg(path + ".tmp"));
            File.Move(path + ".tmp", path, true);

            var settingsPath = Path.Combine(track, "route_0", name + ".xml");
            var settings = PortFiles.ReadXml(settingsPath);
            var patches = settings.Descendants("interactiveWaterPatch").ToArray();
            foreach (var patch in patches)
            {
                if ((string?)patch.Attribute("type") is not ("water" or "water_shore")) throw new InvalidDataException("Unexpected source water type.");
                patch.SetAttributeValue("type", Type);
            }
            WriteSettings(settings, settingsPath + ".tmp");
            File.Move(settingsPath + ".tmp", settingsPath, true);
            reports.Add(new { File = name, Materials = materials, Vertices = vertices, Patches = patches.Length });
        }
        return new { WaterType = Type, SourceColoursPreserved = true, GeometryAndBordersPreserved = true, Files = reports, RuntimeValidated = false };
    }

    internal static void WriteSettings(XDocument settings, string path)
    {
        PortFiles.WriteXml(settings, path, EgoEngineLibrary.Xml.XmlType.Text);
        // Use a text-only reader: the converter's reader accepts binary formats
        // too, which allowed the previous water-format regression to pass.
        if (!XNode.DeepEquals(settings.Root, XDocument.Load(path).Root))
            throw new InvalidDataException("Water settings changed on serialization.");
    }

    internal static int ExpandDirections(XDocument doc, bool interactive)
    {
        int total = 0, prefix = interactive ? 24 : 20;
        string[] semantics = interactive ? ["Vertex", "Color", "ST", "ST", "Normal", "Tangent", "Binormal"] : ["Vertex", "Color", "ST", "Normal", "Tangent", "Binormal"];
        foreach (var block in doc.Descendants("DATABLOCK"))
        {
            var fields = block.Elements("DATABLOCKSTREAM").ToArray();
            if (!fields.Select(n => (string)n.Attribute("renderType")!).SequenceEqual(semantics)) throw new InvalidDataException("Unexpected water vertex semantics.");
            int count = (int)block.Attribute("elementCount")!, stride = prefix + 24, targetStride = prefix + 36;
            var bytes = Convert.FromHexString(string.Concat(block.Element("DATABLOCKDATA")!.Value.Where(c => !char.IsWhiteSpace(c))));
            if (count < 1 || bytes.Length != checked(count * stride) || fields.Any(n => (int)n.Attribute("stride")! != stride)) throw new InvalidDataException("Invalid water vertex buffer.");
            string[] types = interactive ? ["float3", "uint_color_argb", "half2", "half2", "half4", "half4", "half4"] : ["float3", "uint_color_argb", "half2", "half4", "half4", "half4"];
            int[] offsets = interactive ? [0, 12, 16, 20, 24, 32, 40] : [0, 12, 16, 20, 28, 36];
            if (!fields.Select(n => (string)n.Attribute("dataType")!).SequenceEqual(types) || !fields.Select(n => (int)n.Attribute("offset")!).SequenceEqual(offsets)) throw new InvalidDataException("Unsupported water vertex layout.");
            var converted = new byte[checked(count * targetStride)];
            for (int vertex = 0; vertex < count; vertex++)
            {
                bytes.AsSpan(vertex * stride, prefix).CopyTo(converted.AsSpan(vertex * targetStride));
                for (int direction = 0; direction < 3; direction++)
                for (int component = 0; component < 3; component++)
                {
                    float value = (float)BinaryPrimitives.ReadHalfBigEndian(bytes.AsSpan(vertex * stride + prefix + direction * 8 + component * 2));
                    if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite water direction.");
                    BinaryPrimitives.WriteSingleBigEndian(converted.AsSpan(vertex * targetStride + prefix + direction * 12 + component * 4), value);
                }
            }
            foreach (var field in fields) field.SetAttributeValue("stride", targetStride);
            for (int i = 0; i < 3; i++)
            {
                fields[fields.Length - 3 + i].SetAttributeValue("dataType", "float3");
                fields[fields.Length - 3 + i].SetAttributeValue("offset", prefix + i * 12);
            }
            block.SetAttributeValue("size", converted.Length);
            block.Element("DATABLOCKDATA")!.Value = EgoEngineLibrary.Helper.HexHelper.ByteArrayToHexViaLookup32(converted);
            total += count;
        }
        return total;
    }
}
