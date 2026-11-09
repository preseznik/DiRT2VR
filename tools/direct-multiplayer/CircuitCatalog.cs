using System.Data;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Data;

namespace DiRT2VR.Multiplayer;

internal sealed record CircuitCatalog(int Model, int Track, int Location)
{
    internal static string Discipline(Layout layout, bool external)
    {
        if (!external) return !layout.Circuit ? "raid" : layout.Discipline == "Landrush" ? "landrush" : "rallycross";
        return layout.Discipline switch {
            "Rally" => "rally", "Trailblazer" => "trailblazer", "Rallycross" => "rallycross",
            "Landrush" => "landrush", "Point-to-point" => "raid", "Head-to-head" => layout.Circuit ? "rallycross" : "raid",
            _ => "unsupported"
        };
    }
    internal static string WriteSchema(string privateDirectory)
    {
        string path = Path.Combine(privateDirectory, "direct-schema.xml");
        using var embedded = typeof(CircuitCatalog).Assembly.GetManifestResourceStream("DirectCircuitSchema")!;
        using var output = File.Open(path, FileMode.CreateNew, FileAccess.Write);
        embedded.CopyTo(output);
        return path;
    }

    internal static int VehicleId(DatabaseFile database, Player selection)
    {
        if (selection.CustomCar is not { } car) return selection.Car;
        if (!Grid.ValidCar(selection.Car, car)) throw new IOException("Invalid custom car in the Direct roster.");
        var registered = One(database, "vehicle", r => (string)r["file_string"] == car.Code && !(bool)r["swap_with_dummy"]);
        int id = (int)registered["id"];
        _ = One(database, "vehicle_livery", r => (int)r["vehicle_id"] == id && (bool)r["is_default_livery"] &&
            (string)r["file_string"] == car.Code + "_00");
        return id;
    }

    internal static (int Race, int VehicleClass) RaceEvent(DatabaseFile database, int model, int hostCar, int guestCar,
        string? customDiscipline = null, bool alternateRallycross = false)
    {
        int ClassOf(int car)
        {
            var vehicle = One(database, "vehicle", r => (int)r["id"] == car && !(bool)r["swap_with_dummy"]);
            _ = One(database, "vehicle_livery", r => (int)r["vehicle_id"] == car && (bool)r["is_default_livery"]);
            return (int)vehicle["vehicle_class_id"];
        }
        int host = ClassOf(hostCar);
        int guest = hostCar == guestCar ? host : ClassOf(guestCar);
        // Prefer the exact class for like cars. The stock mixed events group
        // Baja with Raid T1, and trophy trucks with class 1 buggies.
        (int native, int netClass) = (Math.Min(host, guest), Math.Max(host, guest)) switch
        {
            (16, 16) => (0, 16), (17, 17) => (1, 17), (18, 18) => (2, 18),
            (19, 19) => (4, 19), (20, 20) => (5, 20), (21, 21) => (3, 21), (22, 22) => (6, 22),
            (19, 20) => (7, 23), (21, 22) => (8, 24),
            _ => throw new IOException("These two cars do not share a stock multiplayer event class.")
        };
        // Built-in Landrush circuits also have a Rallycross event for Rallycross cars.
        if (alternateRallycross && customDiscipline == "landrush" && netClass == 17) customDiscipline = "rallycross";
        DataRow race;
        if (customDiscipline == null)
        {
            var track = One(database, "track_model", r => (int)r["id"] == model);
            race = One(database, "net_race_type", r => (int)r["discipline"] == (int)track["discipline_id"] && (int)r["race_type"] == 1);
        }
        else
            race = One(database, "net_race_type", r => (string)r["name_string_id"] == customDiscipline && (int)r["race_type"] == 1);
        int databaseRace = (int)race["id"];
        int nativeRace = (string)race["name_string_id"] switch
        {
            "rally" => 0, "rallycross" => 1, "trailblazer" => 2, "landrush" => 3, "raid" => 4,
            _ => throw new IOException("Direct has no setup for this race discipline.")
        };
        _ = One(database, "net_race_tracks", r => (int)r["track_model_id"] == model && (int)r["net_race_type"] == databaseRace);
        _ = One(database, "net_session_restrictions", r => (int)r["net_race_type_id"] == databaseRace && (int)r["net_vehicle_class_id"] == netClass);
        _ = One(database, "net_round_restrictions", r => (int)r["track_model_id"] == model && (int)r["net_vehicle_class_id"] == netClass);
        return (nativeRace, native);
    }

    internal static CircuitCatalog ReadLayout(DatabaseFile database, Layout layout)
    {
        DataRow model = One(database, "track_model", r => (string)r["folder_string"] == "usa" &&
            (string)r["file_string"] == layout.Folder && (string)r["route_string"] == "route_0");
        return ReadTrack(database, (int)model["id"]);
    }

    internal static CircuitCatalog ReadTrack(DatabaseFile database, int modelId)
    {
        var model = One(database, "track_model", r => (int)r["id"] == modelId);
        DataRow track = One(database, "track", r => (int)r["id"] == (int)model["track_id"]);
        DataRow location = One(database, "location", r => (int)r["id"] == (int)track["location_id"]);
        return new(modelId, (int)track["id"], (int)location["id"]);
    }

    // Only extend route eligibility. The stock event defaults and global
    // vehicle-class rules also apply to the new route.
    internal static CircuitCatalog Register(string privateDatabase, string schema, Layout layout, bool external = false)
    {
        using var database = new DatabaseFile(privateDatabase, schema);
        var selected = ReadLayout(database, layout);
        int modelId = selected.Model;
        if (!external && selected != new CircuitCatalog(184, 81, 12))
            throw new IOException("The installed layout does not match the private Direct track slot.");
        int race = layout.Circuit ? 2 : 5;                // Rallycross or Raid
        int referenceModel = layout.Circuit ? 143 : 132;  // Battersea Bridge or Baja Peninsula Run
        if (!external && !layout.Circuit)
        {
            var round = One(database, "net_event_default", r => (int)r["race_type"] == race &&
                (int)r["round_index"] == 1 && (int)r["track_model_id"] == referenceModel);
            if ((bool)round["staggered_starts"] || (int)round["number_of_laps"] != 0)
                throw new IOException("The stock Raid reference has unexpected start or lap settings.");
        }
        int[] classes = layout.Circuit ? [17] : [19, 20, 21, 22, 23, 24];
        bool changed;
        if (external)
        {
            string name = Discipline(layout, true);
            int raceId = (int)One(database, "net_race_type", r => (string)r["name_string_id"] == name && (int)r["race_type"] == 1)["id"];
            // Respect a pack that already authors its own multiplayer class restrictions.
            var existing = database.Tables["net_race_tracks"]!.Select($"track_model_id = {modelId} AND net_race_type = {raceId}");
            if (existing.Length > 1) throw new IOException("Ambiguous multiplayer event in the external track.");
            if (existing.Length == 1 && database.Tables["net_round_restrictions"]!.Select($"track_model_id = {modelId}").Length > 0)
                return selected;
            var round = One(database, "net_event_default", r => (int)r["race_type"] == raceId && (int)r["round_index"] == 1);
            changed = RegisterReferenceClasses(raceId, (int)round["track_model_id"]);
        }
        else changed = RegisterEvent(race, referenceModel, classes);
        if (!external && layout.Circuit && layout.Discipline == "Landrush")
        {
            const int ensenadaSprint = 127;
            int landrush = (int)One(database, "net_race_type", r =>
                (string)r["name_string_id"] == "landrush" && (int)r["race_type"] == 1)["id"];
            changed |= RegisterReferenceClasses(landrush, ensenadaSprint);
        }
        bool RegisterReferenceClasses(int eventId, int sourceModel)
        {
            var roundClasses = database.Tables["net_round_restrictions"]!.Select($"track_model_id = {sourceModel}");
            var sessionClasses = database.Tables["net_session_restrictions"]!.Select($"net_race_type_id = {eventId}");
            var eligible = roundClasses.Select(r => (int)r["net_vehicle_class_id"])
                .Intersect(sessionClasses.Select(r => (int)r["net_vehicle_class_id"])).Order().ToArray();
            if (eligible.Length == 0) throw new IOException("The stock reference event has no eligible vehicle classes.");
            return RegisterEvent(eventId, sourceModel, eligible);
        }
        if (changed)
        {
            using var output = File.Open(privateDatabase, FileMode.Create, FileAccess.Write);
            database.Write(output);
        }
        return selected;

        bool RegisterEvent(int raceId, int sourceModel, int[] vehicleClasses)
        {
            bool added = EnsureEntry("net_race_tracks", "net_race_type", raceId, sourceModel);
            foreach (int vehicleClass in vehicleClasses)
            {
                _ = One(database, "net_session_restrictions", r =>
                    (int)r["net_race_type_id"] == raceId && (int)r["net_vehicle_class_id"] == vehicleClass);
                added |= EnsureEntry("net_round_restrictions", "net_vehicle_class_id", vehicleClass, sourceModel);
            }
            return added;
        }

        bool EnsureEntry(string tableName, string column, int value, int sourceModel)
        {
            var table = database.Tables[tableName]!;
            // Require the reference event to exist in this database.
            var reference = One(database, tableName, r => (int)r[column] == value && (int)r["track_model_id"] == sourceModel);
            var existing = table.Rows.Cast<DataRow>().Where(r =>
                (int)r[column] == value && (int)r["track_model_id"] == modelId).ToArray();
            if (existing.Length == 1) return false;
            if (existing.Length > 1) throw new IOException("Ambiguous custom circuit in " + tableName);
            var added = table.NewRow();
            added.ItemArray = reference.ItemArray;
            added["id"] = checked(table.Rows.Cast<DataRow>().Max(r => (int)r["id"]) + 1);
            added["track_model_id"] = modelId;
            table.Rows.Add(added);
            return true;
        }
    }

    static DataRow One(DatabaseFile database, string name, Func<DataRow,bool> matches)
    {
        var rows = database.Tables[name]?.Rows.Cast<DataRow>().Where(matches).Take(2).ToArray();
        return rows?.Length == 1 ? rows[0] : throw new IOException("Missing or ambiguous circuit entry in " + name);
    }
}
