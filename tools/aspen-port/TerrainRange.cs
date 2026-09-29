using System.Xml.Linq;

internal static class TerrainRange
{
    // Accepted Snowmass Sprint ranges cover the authored venue. Keep the other
    // layouts and all unrelated route controls at their source values.
    internal static void Convert(XDocument route)
    {
        var blocks = route.Root!.Elements().Where(n => n.Attribute("track_lod_dist") is not null).ToArray();
        if (blocks.Length != 3 || blocks.Any(n => n.Attribute("track_cull_dist") is null))
            throw new InvalidDataException("Unexpected Snowmass Sprint route overrides.");
        foreach (var block in blocks)
        {
            block.SetAttributeValue("track_lod_dist", "1800.0");
            block.SetAttributeValue("track_cull_dist", "2000.0");
        }
    }
}
