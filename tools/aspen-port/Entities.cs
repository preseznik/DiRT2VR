using System.Xml.Linq;
using System.Globalization;
using EgoEngineLibrary.Graphics.Pssg;

internal static class Entities
{
    static readonly Dictionary<string, HashSet<string>> TextFields = new(StringComparer.Ordinal);
    // The converter's default schema treats unknown XML values as hex byte strings.
    // Aspen's CSSG XML instead contains ordinary names and numeric transforms.
    internal static void RegisterSchema()
    {
        var schema = new XElement("PSSGFILE");
        void Add(string name, string strings, string integers = "", string floats = "")
        {
            TextFields[name] = (strings + " " + integers + " " + floats).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            var node = Node(name, "None");
            foreach (var attribute in strings.Split(' ', StringSplitOptions.RemoveEmptyEntries)) node.Add(new XElement("attribute", new XAttribute("name", attribute), new XAttribute("dataType", "String")));
            foreach (var attribute in integers.Split(' ', StringSplitOptions.RemoveEmptyEntries)) node.Add(new XElement("attribute", new XAttribute("name", attribute), new XAttribute("dataType", "Int")));
            foreach (var attribute in floats.Split(' ', StringSplitOptions.RemoveEmptyEntries)) node.Add(new XElement("attribute", new XAttribute("name", attribute), new XAttribute("dataType", "Float")));
            schema.Add(node);
        }
        XElement Node(string name, string type) => new("node", new XAttribute("name", name), new XAttribute("dataType", type), new XAttribute("elementsPerRow", 16), new XAttribute("linkAttributeName", ""));
        Add("TEMPLATEBASICENTITYINSTANCE", "id uri mode_layer", "instance_tag");
        Add("TEMPLATEENTITYINSTANCE", "id uri mode_layer", "instanceID instance_tag staticVis");
        Add("TEMPLATEENTITYREFERENCE", "id uri", "allocAlt");
        Add("TEMPLATECLOTHINSTANCE", "id uri", "startsActive collisionDetection");
        Add("TEMPLATECLOTHATTACHINSTANCE", "id instance name", "");
        Add("TEMPLATECLOTHTYPEPOOL", "id file shaderfront shaderback", "");
        Add("TEMPLATEENTITY", "id PredictiveWakeUp");
        Add("TEMPLATESUBENTITY", "id");
        Add("TEMPLATERIGIDBODY", "id props", "infMass");
        Add("TEMPLATERIGIDBODYSHAPE", "uri");
        Add("TEMPLATETRANSFORMABLE", "uri sid");
        Add("TEMPLATEBEHAVIOUR", "id impulse typeuri");
        Add("TEMPLATEBEHAVIOURTYPE", "id");
        Add("TEMPLATECOMPONENTCHANGE", "componenturi operation");
        Add("TEMPLATESHAPEBOX", "id props", floats: "width height length shell");
        Add("TEMPLATESHAPECYLINDER", "id props", floats: "height radius shell");
        Add("TEMPLATESHAPESPHERE", "id props", floats: "radius shell");
        Add("TEMPLATERENDERABLE", "id uri");
        Add("TEMPLATELINKPOINT", "id");
        Add("TEMPLATECONSTRAINT", "id body1uri body2uri props");
        schema.Add(Node("TEMPLATETRANSFORM", "Float"));
        using var memory = new MemoryStream(); new XDocument(schema).Save(memory); memory.Position = 0;
        PssgSchema.LoadSchema(memory);
    }

    internal static void ValidateText(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            string name = element.Name.LocalName;
            if (name is "PSSGFILE" or "PSSGDATABASE") continue;
            if (name == "TEMPLATETRANSFORM")
            {
                var values = element.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length != 16 || values.Any(s => !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) || !float.IsFinite(f)))
                    throw new InvalidDataException("Invalid CSSG entity transform.");
                continue;
            }
            if (!TextFields.TryGetValue(name, out var fields) || element.Attributes().Any(a => !fields.Contains(a.Name.LocalName)))
                throw new InvalidDataException("Unsupported CSSG text fields: " + name);
        }
    }

    internal static void Convert(XDocument file, bool objectTypes = false)
    {
        var root = file.Root?.Element("PSSGDATABASE") ?? throw new InvalidDataException("Missing entity database.");
        root.SetAttributeValue("scale", "1 1 1"); root.SetAttributeValue("up", "0 1 0");
        foreach (var attribute in root.Descendants().Attributes("PredictiveWakeUp").ToArray()) attribute.Remove();
        foreach (var attribute in root.Descendants().Attributes().Where(a => a.Name == "instance_tag" || a.Name == "mode_layer").ToArray()) attribute.Remove();
        const string identity = "1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1";
        int attachment = 0;
        foreach (var cloth in root.Descendants("TEMPLATECLOTHINSTANCE"))
        {
            if (cloth.Element("TEMPLATETRANSFORM") is null) cloth.AddFirst(new XElement("TEMPLATETRANSFORM", identity));
            foreach (var attach in cloth.Elements("TEMPLATECLOTHATTACHINSTANCE")) attach.SetAttributeValue("id", "aspen-attachment-" + attachment++);
        }
        var children = root.Elements().ToArray();
        if (children.Any(e => e.Name == "LIBRARY")) throw new InvalidDataException("Expected Aspen's flat CSSG entity layout.");
        root.RemoveNodes();
        foreach (var group in children.GroupBy(e => e.Name.LocalName).OrderBy(g => g.Key))
            root.Add(new XElement("LIBRARY", new XAttribute("type", group.Key), group));
        if (objectTypes) return; // Object-local identities are scoped inside each template.
        var ids = root.Descendants().Attributes("id").Select(a => a.Value).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count() != ids.Length) throw new InvalidDataException("Invalid entity identity.");
        var known = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var reference in root.Descendants().Attributes().Where(a => a.Name == "uri" || a.Name == "instance"))
            if (reference.Value.StartsWith('#') && !known.Contains(reference.Value[1..])) throw new InvalidDataException("Unresolved entity reference: " + reference.Value);
    }

    internal static void Verify(XDocument before, XDocument after)
    {
        XDocument Normalize(XDocument document)
        {
            var copy = new XDocument(document);
            string Floats(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => BitConverter.SingleToInt32Bits(float.Parse(s, CultureInfo.InvariantCulture)).ToString("X8")));
            foreach (var transform in copy.Descendants("TEMPLATETRANSFORM")) transform.Value = Floats(transform.Value);
            foreach (var attribute in copy.Descendants("PSSGDATABASE").Attributes().Where(a => a.Name == "scale" || a.Name == "up")) attribute.Value = Floats(attribute.Value);
            foreach (var attribute in copy.Descendants().Where(e => e.Name.LocalName.StartsWith("TEMPLATESHAPE")).Attributes().Where(a => a.Name.LocalName is "width" or "height" or "length" or "radius" or "shell")) attribute.Value = Floats(attribute.Value);
            return copy;
        }
        if (!XNode.DeepEquals(Normalize(before).Root, Normalize(after).Root))
            throw new InvalidDataException("Entity identities/transforms changed during binary serialization.");
    }
}
