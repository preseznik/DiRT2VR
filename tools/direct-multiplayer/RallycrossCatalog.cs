using DiRT2VR.CustomTracks;

namespace DiRT2VR.Multiplayer;

public sealed record RallycrossChoice(int Id, string Name, string? LayoutId = null, bool Circuit = true, DirectCustomCar? CustomCar = null, string? Discipline = null)
{
    public override string ToString() => Name;
}

public static class RallycrossCatalog
{
    internal static bool Supports(Grid grid)
    {
        if (!Grid.ValidSelection(grid.Track, grid.Layout) || grid.Track == DirectStockCatalog.PrototypeId ||
            !Grid.ValidCar(grid.Host.Car, grid.Host.CustomCar) ||
            grid.Guest is { } guest && !Grid.ValidCar(guest.Car, guest.CustomCar)) return false;
        static string? Group(Player? player) => player is null ? null : player.CustomCar is { } car
            ? car.Discipline() : DirectStockCatalog.Cars[player.Car].Discipline;
        string? left = Group(grid.Host), right = Group(grid.Guest);
        if (left != null && right != null && left != right) return false;
        string? cars = left ?? right;
        if (cars == null) return true; // Imported car classes come from the prepared database.
        string? race;
        if (grid.Layout is { } pack)
        {
            if (!TrackPacks.IsLayout(pack.Id)) return pack.ExternalCircuit.HasValue;
            var layout = DirectLayout.Pack(pack.Id).GetLayout(pack.Id);
            race = CircuitCatalog.Discipline(layout, pack.ExternalCircuit.HasValue);
            if (!pack.ExternalCircuit.HasValue && layout.Circuit && cars == "Rallycross") return true;
        }
        else race = DirectStockCatalog.Tracks[grid.Track].Discipline!.ToLowerInvariant();
        return race is "landrush" or "raid" ? cars is "Landrush" or "Raid" : string.Equals(race, cars, StringComparison.OrdinalIgnoreCase);
    }

    // Stock model IDs accepted by the Direct Rallycross event. Custom layouts use their pack digest.
    public static IReadOnlyList<RallycrossChoice> Tracks { get; } = Array.AsReadOnly<RallycrossChoice>([
        new(143, "London – Battersea Bridge"), new(144, "London – Battersea Air"),
        new(171, "London – Battersea Bridge B"), new(141, "Japan – Shibuya"),
        new(142, "Japan – Shibuya B"), new(145, "USA – Marina Air"),
        new(146, "USA – Marina Sprint"), new(182, "USA – Stadium Sprint")
    ]);

    public static RallycrossChoice Circuit(string id)
    {
        var pack = DirectLayout.Pack(id);
        var layout = pack.GetLayout(id);
        string label = pack.Layouts.Length == 1 ? pack.Name : pack.Name + " – " +
            (pack.Layouts.Count(other=>other.Name==layout.Name)>1 ? layout.Condition : layout.Name);
        return new(0, label + (pack.External == null ? " (custom)" : " (external)"), id, layout.Circuit);
    }

    public static RallycrossChoice Circuit(DirectLayout selection) =>
        selection.ExternalCircuit is bool circuit && !TrackPacks.IsLayout(selection.Id)
            ? new(0, selection.Id + " (external)", selection.Id, circuit) : Circuit(selection.Id);

    public static IEnumerable<RallycrossChoice> InstalledCircuits(string game)
    {
        yield return Circuit(DirectLayout.SpaId);
        foreach (var pack in TrackPacks.DirectRacePacks.Where(pack => pack != TrackPacks.Spa))
        {
            PackReceipt receipt;
            try
            {
                if (!File.Exists(SafeFiles.Inside(game,pack.Receipt))) continue;
                receipt = pack.Read(game,verify:false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var layout in pack.ReceiptLayouts(receipt))
                if (pack.SupportsRace(receipt,layout.Id)) yield return Circuit(layout.Id);
        }
        foreach (var pack in TrackPacks.All.Where(p => p.External?.BelongsToInstallation(game) == true && p.Modes.Contains("desktop-race")))
            foreach (var layout in pack.Layouts)
                if (pack.SupportsGridRace(layout.Id)) yield return Circuit(layout.Id);
    }

    // One entry per stock Rallycross model with a default livery.
    public static IReadOnlyList<RallycrossChoice> Cars { get; } = Array.AsReadOnly<RallycrossChoice>([
        new(88, "Subaru Impreza STI Group N"), new(85, "Subaru Impreza WRX STI"),
        new(121, "BMW Z4 M Coupe Motorsport"), new(79, "Colin McRae R4"),
        new(128, "Ford RS200 Evolution"), new(127, "MG Metro 6R4"),
        new(124, "Mitsubishi Eclipse GT"), new(134, "Mitsubishi Lancer Evolution IX"),
        new(82, "Mitsubishi Lancer Evolution X"), new(115, "Nissan 350Z"),
        new(118, "Pontiac Solstice GXP")
    ]);
}
