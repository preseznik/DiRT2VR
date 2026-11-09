using DiRT2VR.CustomTracks;

namespace DiRT2VR.Multiplayer;

public sealed record RallycrossChoice(int Id, string Name, string? LayoutId = null, bool Circuit = true, DirectCustomCar? CustomCar = null)
{
    public override string ToString() => Name;
}

public static class RallycrossCatalog
{
    internal static bool Supports(Grid grid) =>
        (grid.Layout == null ? Tracks.Any(track => track.Id == grid.Track) : grid.Layout.ExternalCircuit == null && grid.IsCircuit) &&
        Cars.Any(car => car.Id == grid.Host.Car) && (grid.Guest == null || Cars.Any(car => car.Id == grid.Guest.Car));

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
        foreach (var pack in TrackPacks.All.Where(p => p.External != null))
            foreach (var layout in pack.Layouts) yield return Circuit(layout.Id);
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
