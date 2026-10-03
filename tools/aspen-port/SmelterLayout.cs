internal sealed record SmelterLayout(string Id, string Route, string Name, string Discipline, string Condition)
{
    // track_model.file_string is limited to 18 characters in DiRT 2.
    internal string Directory => "d2vr_smelter_" + Route[6..];
    // Verified against the local DiRT 3 database. An entry is not a claim of support.
    internal static readonly SmelterLayout[] All = [
        new("smelter-county-loop", "route_0", "County Loop", "rallycross", "day"),
        new("smelter-portage-canal", "route_1", "Portage Canal", "rallycross", "day"),
        new("smelter-houghton-sprint", "route_2", "Houghton Sprint", "rallycross", "wet"),
        new("smelter-waterfront-park", "route_3", "Waterfront Park", "rallycross", "wet"),
        new("smelter-copper-run", "route_6", "Copper Run", "landrush", "day_alt"),
        new("smelter-maple-woods", "route_7", "Maple Woods", "landrush", "day_alt"),
        new("smelter-atlantic-mill", "route_8", "Atlantic Mill", "landrush", "day"),
        new("smelter-coles-creek", "route_9", "Cole's Creek", "landrush", "day"),
        new("smelter-dredger-duel", "route_4", "Dredger Duel", "head_2_head", "day_alt"),
        new("smelter-furnace-duel", "route_5", "Furnace Duel", "head_2_head", "day_alt")];
    internal static SmelterLayout CountyLoop => All[0];
}
