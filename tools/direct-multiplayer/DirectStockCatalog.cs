using System.Globalization;
using System.Text.Json;

namespace DiRT2VR.Multiplayer;

public static class DirectStockCatalog
{
    // The desktop-only prototype has no stock track-model ID.
    public const int PrototypeId = -1;
    public static RallycrossChoice Prototype { get; }
    public static IReadOnlyDictionary<int, RallycrossChoice> Tracks { get; }
    public static IReadOnlyDictionary<int, RallycrossChoice> Cars { get; }
    internal static IReadOnlySet<string> StockCarCodes { get; }
    internal static IReadOnlyDictionary<string, string> CarDisciplines { get; }

    static DirectStockCatalog()
    {
        using var stream = typeof(DirectStockCatalog).Assembly.GetManifestResourceStream("RaceCatalog")!;
        using var catalog = JsonDocument.Parse(stream);
        var tracks = new Dictionary<int, RallycrossChoice>();
        RallycrossChoice? prototype = null;
        foreach (var entry in catalog.RootElement.GetProperty("Tracks").EnumerateArray())
        {
            string key = entry.GetProperty("Id").GetString()!;
            string label = entry.GetProperty("Label").GetString()!;
            bool circuit = entry.GetProperty("Circuit").GetBoolean();
            if (key == "d2vr_test")
            {
                prototype = new(PrototypeId, label, Circuit: circuit);
                continue;
            }
            int id = int.Parse(key, NumberStyles.None, CultureInfo.InvariantCulture);
            tracks.Add(id, new(id, label, Circuit: circuit, Discipline: entry.GetProperty("Event").GetString()));
        }
        Prototype = prototype ?? throw new InvalidDataException("Missing prototype layout in the Race/Practice catalog.");
        Tracks = tracks;
        var cars = catalog.RootElement.GetProperty("Cars");
        CarDisciplines = cars.EnumerateArray().ToDictionary(entry => entry.GetProperty("Code").GetString()!,
            entry => entry.GetProperty("Event").GetString()!, StringComparer.Ordinal);
        StockCarCodes = CarDisciplines.Keys.ToHashSet(StringComparer.Ordinal);
        Cars = cars.EnumerateArray()
            .Select(entry => new RallycrossChoice(entry.GetProperty("VehicleId").GetInt32(), entry.GetProperty("Label").GetString()!,
                Discipline: entry.GetProperty("Event").GetString()))
            .ToDictionary(car => car.Id);
    }

    public static RallycrossChoice Track(int id) => id == PrototypeId ? Prototype : Tracks[id];
}
