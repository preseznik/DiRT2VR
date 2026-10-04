using System.Xml.Linq;

internal static class SmelterVisibility
{
    internal static void Route(XDocument route)
    {
        var blocks = route.Root!.Elements().Where(n => n.Attribute("track_lod_dist") is not null).ToArray();
        if (blocks.Length == 0 || route.Root.Element("default")?.Attribute("track_lod_dist") is null || blocks.Any(n => n.Attribute("track_cull_dist") is null || n.Attribute("world_cull_dist") is null || n.Attribute("main_obj_size") is null))
            throw new InvalidDataException("Unexpected Smelter visibility controls.");
        // Baked scenery lives in the terrain tiles, so their culling distances
        // must cover the venue too. Keep shadow/reflection distances unchanged.
        foreach (var block in blocks)
        {
            block.SetAttributeValue("track_lod_dist", "1800.0");
            block.SetAttributeValue("track_cull_dist", "2000.0");
            block.SetAttributeValue("world_cull_dist", "2000.0");
            block.SetAttributeValue("main_obj_size", "0.0");
        }
    }
    internal static int Barriers(XDocument attributes)
    {
        int count = 0;
        foreach (var model in attributes.Root!.Elements("ornament_attributes"))
        {
            string name = (string)model.Attribute("name")!;
            if (!(name.StartsWith("core_barr_", StringComparison.Ordinal) || name.StartsWith("junk_barrier_", StringComparison.Ordinal) || name.StartsWith("armco_barrel_", StringComparison.Ordinal) || name.StartsWith("rusty_fence_", StringComparison.Ordinal))) continue;
            foreach (string field in new[] { "lod_distance_00", "lod_distance_01", "lod_distance_02" })
                if (model.Attribute(field) is null) throw new InvalidDataException("Missing barrier detail distance: " + name);
            model.SetAttributeValue("lod_distance_00", "1800.00");
            model.SetAttributeValue("lod_distance_01", "1900.00");
            model.SetAttributeValue("lod_distance_02", "2000.00");
            count++;
        }
        if (count == 0) throw new InvalidDataException("No County Loop barrier detail settings.");
        return count;
    }
}
