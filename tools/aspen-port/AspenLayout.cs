using System.Globalization;
using System.Xml.Linq;

// Each port gets its own game directory. Output routes stay normalized to
// route_0, so the proven conversion stages do not depend on a source index.
internal sealed record AspenLayout
{
    internal string Id { get; }
    internal string SourceRoute { get; }
    internal string DirectoryName { get; }
    internal string StringId { get; }
    internal string DisplayName { get; }
    internal string EffectsPath => $"tracks/usa/{DirectoryName}/aspen-effects/";
    private AspenLayout(string id, string route, string directory, string key, string name)
        => (Id, SourceRoute, DirectoryName, StringId, DisplayName) = (id, route, directory, key, name);
    internal static readonly AspenLayout Lakeside = new("aspen-lakeside", "route_0", "d2vr_aspen", "aspen_lakeside", "LAKESIDE");
    internal static readonly AspenLayout LakeView = new("aspen-lake-view", "route_1", "d2vr_aspen_lv", "aspen_lake_view", "LAKE VIEW");
    internal static readonly AspenLayout SnowmassSprint = new("aspen-snowmass-sprint", "route_2", "d2vr_aspen_ss", "aspen_snowmass_sprint", "SNOWMASS SPRINT");
    internal static readonly AspenLayout SnowmassLoop = new("aspen-snowmass-loop", "route_3", "d2vr_aspen_sl", "aspen_snowmass_loop", "SNOWMASS LOOP");
    internal static AspenLayout Parse(string name) => name switch {
        "lakeside" => Lakeside,
        "lake-view" => LakeView,
        "snowmass-sprint" => SnowmassSprint,
        "snowmass-loop" => SnowmassLoop,
        _ => throw new InvalidDataException("Unsupported Aspen layout: " + name)
    };
    internal static float RouteLength(XDocument progress)
    {
        var track = progress.Root?.Element("track");
        if ((string?)track?.Attribute("type") != "circuit" ||
            !float.TryParse((string?)track.Attribute("total_distance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float distance) ||
            !float.IsFinite(distance) || distance <= 0)
            throw new InvalidDataException("Expected a circuit with a positive route length.");
        return distance;
    }
}
