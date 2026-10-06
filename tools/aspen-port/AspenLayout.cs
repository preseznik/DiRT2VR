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
    internal static readonly AspenLayout EagleHillRise = new("aspen-eagle-hill-rise", "route_6", "d2vr_aspen_ehr", "aspen_eagle_hill_rise", "EAGLE HILL RISE");
    internal static readonly AspenLayout EagleHillLoop = new("aspen-eagle-hill-loop", "route_7", "d2vr_aspen_ehl", "aspen_eagle_hill_loop", "EAGLE HILL LOOP");
    internal static readonly AspenLayout BrushCreekSprint = new("aspen-brush-creek-sprint", "route_8", "d2vr_aspen_bcs", "aspen_brush_creek_sprint", "BRUSH CREEK SPRINT");
    internal static readonly AspenLayout BrushCreekDash = new("aspen-brush-creek-dash", "route_9", "d2vr_aspen_bcd", "aspen_brush_creek_dash", "BRUSH CREEK DASH");
    internal static readonly AspenLayout ButtermilkDescent = new("aspen-buttermilk-descent", "route_4", "d2vr_aspen_bd", "aspen_buttermilk_descent", "BUTTERMILK DESCENT");
    internal static readonly AspenLayout ButtermilkClimb = new("aspen-buttermilk-climb", "route_5", "d2vr_aspen_bc", "aspen_buttermilk_climb", "BUTTERMILK CLIMB");
    internal bool PracticeTest => SourceRoute is not ("route_0" or "route_1" or "route_2" or "route_3");
    internal int DonorModel => SourceRoute is "route_6" or "route_7" or "route_8" or "route_9" ? 127 : 144;
    internal static AspenLayout Parse(string name) => name switch {
        "lakeside" => Lakeside,
        "lake-view" => LakeView,
        "snowmass-sprint" => SnowmassSprint,
        "snowmass-loop" => SnowmassLoop,
        "eagle-hill-rise" => EagleHillRise,
        "eagle-hill-loop" => EagleHillLoop,
        "brush-creek-sprint" => BrushCreekSprint,
        "brush-creek-dash" => BrushCreekDash,
        "buttermilk-descent" => ButtermilkDescent,
        "buttermilk-climb" => ButtermilkClimb,
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
