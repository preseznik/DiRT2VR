using System.Xml.Linq;

internal static class TerrainRange
{
    // Accepted Snowmass Sprint ranges cover the authored venue. Keep the other
    // accepted layouts at their source values. New practice layouts opt in explicitly.
    internal static void Convert(XDocument route, bool newLayout = false)
    {
        var blocks = route.Root!.Elements().Where(n => n.Attribute("track_lod_dist") is not null).ToArray();
        if ((newLayout ? blocks.Length == 0 : blocks.Length != 3) || blocks.Any(n => n.Attribute("track_cull_dist") is null))
            throw new InvalidDataException("Unexpected Aspen terrain range settings.");
        foreach (var block in blocks)
        {
            block.SetAttributeValue("track_lod_dist", "1800.0");
            block.SetAttributeValue("track_cull_dist", "2000.0");
        }
    }
}
