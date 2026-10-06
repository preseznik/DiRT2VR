internal sealed record AspenCondition(string Name, string Suffix, bool Night)
{
    internal static readonly AspenCondition Morning = new("morning", "day", false);
    internal static readonly AspenCondition Evening = new("evening", "day_alt", false);
    internal static readonly AspenCondition NightTime = new("night", "night", true);
    internal static readonly AspenCondition Overcast = new("overcast", "snowing", false);
    internal static AspenCondition Parse(string name) => name switch {
        "morning" => Morning, "evening" => Evening, "night" => NightTime, "overcast" => Overcast,
        _ => throw new InvalidDataException("Unsupported Aspen condition: " + name)
    };
    internal static AspenCondition Default(AspenLayout layout) => layout.SourceRoute switch {
        "route_0" => NightTime, "route_1" => Morning, "route_2" => Evening, "route_3" => Overcast,
        "route_6" or "route_7" => NightTime,
        "route_8" or "route_9" => Evening,
        "route_4" or "route_5" => Overcast,
        _ => throw new InvalidDataException("No default condition for layout.")
    };
    internal bool Include(string name)
    {
        // The common AO atlas is shared across all conditions in the source.
        if (name == "trackao_day.pssg") return true;
        foreach (string suffix in new[] { "day_alt", "day", "night", "snowing", "wet" })
            if (name.Contains("_" + suffix + ".", StringComparison.Ordinal)) return suffix == Suffix;
        return true;
    }
}
