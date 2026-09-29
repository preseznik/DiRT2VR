using System.Xml.Linq;

internal static class ShaderConversion
{
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Object_Dynamic_skin.fx"] = "object_simple_skin.fx", ["Object_skin.fx"] = "object_simple_skin.fx",
        ["Object.fx"] = "object_simple.fx", ["object_teardrop_flag_Norm.fx"] = "object_teardrop_flag_nm.fx",
        ["Object_Emis.fx"] = "object_simple_emissive.fx", ["Object_Norm.fx"] = "object_simple_dxt5nm.fx",
        ["object_x2.fx"] = "object_simple_blended.fx", ["Object_X2_Emis.fx"] = "object_simple_emissive.fx",
        ["Object_RefBlend.fx"] = "object_simple_dxt5nm_blended_reflective.fx", ["Object_RefEmis.fx"] = "object_simple_reflective.fx",
        ["Object_Dynamic.fx"] = "object_simple.fx", ["Object_Ref.fx"] = "object_simple_reflective.fx",
        ["Object_2Sided.fx"] = "object_simple.fx", ["Object_2SidedCov.fx"] = "object_simple.fx",
        ["terrain_rockbank.fx"] = "terrain_edge_nm.fx", ["terrain_rockbank_flat.fx"] = "terrain_edge.fx",
        ["terrain_infield_overlay_feather.fx"] = "terrain_infield.fx",
        // Snowy trunks carry normals (36-byte vertices). Mapping them to the
        // 24-byte leaf shader poisons DiRT 2's shared tree input layout.
        ["foliage_trunk_snow.fx"] = "foliage_environment.fx",
        ["terrain_dynamic_reveal_tessellation.fx"] = "terrain_road.fx"
    };
    internal static void Convert(XDocument file, Dictionary<string, XElement> templates, string name, List<object> report)
    {
        var groups = file.Descendants("SHADERGROUP").ToArray();
        // Snapshot source bindings: aliases may collide with another source group's ID.
        var sourceInstances = file.Descendants("SHADERINSTANCE").ToLookup(e => (string?)e.Attribute("shaderGroup"));
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var sourceId = (string)group.Attribute("id")!;
            var targetId = templates.ContainsKey(sourceId) ? sourceId : Aliases.GetValueOrDefault(sourceId);
            if (targetId is null || !templates.TryGetValue(targetId, out var template)) throw new InvalidDataException($"No shader mapping for {name}: {sourceId}");
            var sourceInputs = group.Elements("SHADERINPUTDEFINITION").ToArray();
            var targetInputs = template.Elements("SHADERINPUTDEFINITION").ToArray();
            var removed = new HashSet<string>(); int instances = 0;
            foreach (var instance in sourceInstances["#" + sourceId])
            {
                foreach (var input in instance.Elements("SHADERINPUT").ToArray())
                {
                    var oldIndex = (int)input.Attribute("parameterID")!;
                    if (oldIndex < 0 || oldIndex >= sourceInputs.Length) throw new InvalidDataException("Invalid source shader parameter.");
                    var definition = sourceInputs[oldIndex];
                    var parameter = (string)definition.Attribute("name")!;
                    var index = Array.FindIndex(targetInputs, e => (string?)e.Attribute("name") == parameter);
                    if (index < 0) { removed.Add(parameter); input.Remove(); continue; }
                    if ((string?)definition.Attribute("type") != (string?)targetInputs[index].Attribute("type") ||
                        (string?)definition.Attribute("format") != (string?)targetInputs[index].Attribute("format"))
                        throw new InvalidDataException($"Shader parameter type mismatch: {name}/{sourceId}/{parameter}");
                    input.SetAttributeValue("parameterID", index);
                }
                instance.SetAttributeValue("shaderGroup", "#" + targetId);
                instance.SetAttributeValue("parameterCount", targetInputs.Length);
                instance.SetAttributeValue("parameterSavedCount", instance.Elements("SHADERINPUT").Count());
                instances++;
            }
            // Multiple DiRT 3 shaders can share one DiRT 2 definition. Keep material IDs unique.
            if (added.Add(targetId)) group.AddBeforeSelf(new XElement(template));
            group.Remove();
            report.Add(new { File = name, Source = sourceId, Target = targetId, Instances = instances, RemovedInputs = removed.Order().ToArray() });
        }
        var definitions = file.Descendants("SHADERGROUP").ToDictionary(e => (string)e.Attribute("id")!);
        foreach (var instance in file.Descendants("SHADERINSTANCE"))
        {
            var id = ((string)instance.Attribute("shaderGroup")!)[1..];
            if (!definitions.TryGetValue(id, out var group)) throw new InvalidDataException("Unresolved shader group: " + id);
            var count = group.Elements("SHADERINPUTDEFINITION").Count();
            var indices = instance.Elements("SHADERINPUT").Select(e => (int)e.Attribute("parameterID")!).ToArray();
            if (indices.Any(i => i < 0 || i >= count) || indices.Distinct().Count() != indices.Length) throw new InvalidDataException("Invalid remapped shader inputs.");
        }
    }
}
