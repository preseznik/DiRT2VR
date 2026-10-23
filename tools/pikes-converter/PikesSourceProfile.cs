using System.Globalization;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

// Source identity and weather travel together: no modern-venue fallback is
// allowed while resolving a vintage material or a condition's baked lighting.
internal sealed record PikesSourceProfile(string Venue, string Condition)
{
    internal static readonly PikesSourceProfile Modern = new("pikes_peak", "day");
    internal static PikesSourceProfile Gravel(string condition)
    {
        if (condition is not ("day" or "day_alt" or "wet"))
            throw new ArgumentException("Expected day, day_alt or wet.");
        return new("pikes_peak_historic_02", condition);
    }
    internal bool IsGravel => Venue == "pikes_peak_historic_02";
    internal string PathIn(string rally, string relative = "") =>
        Files.Inside(rally, "tracks/locations/usa/" + Venue + (relative.Length == 0 ? "" : "/" + relative));
    internal XDocument Lighting(string rally) => ReadXml(PathIn(rally, "lighting_" + Condition + ".xml"));
    internal static XDocument ReadXml(string path)
    {
        using var input = File.OpenRead(path);
        return XDocument.Parse(new XmlFile(input).Document.OuterXml);
    }
    internal static string Parameter(XDocument document, string name) =>
        document.Descendants("param").Single(p => p.Attribute(name) is not null).Attribute(name)!.Value;
    internal float Value(string rally, string name) => float.Parse(Parameter(Lighting(rally), name), CultureInfo.InvariantCulture);
    internal string[] ConditionFiles => ["lighting_" + Condition + ".xml", "ambientmaps_" + Condition + ".pssg",
        "envmap_" + Condition + ".pssg", "route_0/ambient_sh_" + Condition + ".bin",
        "route_0/buildings_lm_" + Condition + ".pssg", "route_0/terrain_lm_" + Condition + ".pssg",
        "route_0/shadow_map_" + Condition + ".clm", "route_0/sky_" + Condition + ".pssg"];
}
