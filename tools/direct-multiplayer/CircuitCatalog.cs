using System.Data;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Data;

namespace DiRT2VR.Multiplayer;

internal sealed record CircuitCatalog(int Model, int Track, int Location)
{
    internal static string WriteSchema(string privateDirectory)
    {
        string path = Path.Combine(privateDirectory, "direct-schema.xml");
        using var embedded = typeof(CircuitCatalog).Assembly.GetManifestResourceStream("DirectCircuitSchema")!;
        using var output = File.Open(path, FileMode.CreateNew, FileAccess.Write);
        embedded.CopyTo(output);
        return path;
    }

    internal static (int Race, int VehicleClass) OffroadEvent(DatabaseFile database, int model, int hostCar, int guestCar, bool customStage)
    {
        int databaseRace = 5, nativeRace = 4; // Custom point-to-point routes use the Raid event.
        if (!customStage)
        {
            var track = One(database, "track_model", r => (int)r["id"] == model);
            var discipline = One(database, "discipline", r => (int)r["id"] == (int)track["discipline_id"]);
            if ((string)discipline["name_string_id"] != "landrush")
                throw new IOException("Direct has no race setup for this stock discipline.");
            databaseRace = 4;
            nativeRace = 3;
        }

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
            (19, 19) => (4, 19), (20, 20) => (5, 20), (21, 21) => (3, 21), (22, 22) => (6, 22),
            (19, 20) => (7, 23), (21, 22) => (8, 24),
            _ => throw new IOException("These two cars do not share a stock off-road event class.")
        };
        _ = One(database, "net_race_tracks", r => (int)r["track_model_id"] == model && (int)r["net_race_type"] == databaseRace);
        _ = One(database, "net_session_restrictions", r => (int)r["net_race_type_id"] == databaseRace && (int)r["net_vehicle_class_id"] == netClass);
        _ = One(database, "net_round_restrictions", r => (int)r["track_model_id"] == model && (int)r["net_vehicle_class_id"] == netClass);
        return (nativeRace, native);
    }

    // Only extend route eligibility. The stock event defaults and global
    // vehicle-class rules also apply to the new route.
    internal static CircuitCatalog Register(string privateDatabase, string schema, Layout layout)
    {
        using var database = new DatabaseFile(privateDatabase, schema);
        DataRow model = One(database, "track_model", r => (string)r["folder_string"] == "usa" &&
            (string)r["file_string"] == layout.Folder && (string)r["route_string"] == "route_0");
        int modelId = (int)model["id"];
        DataRow track = One(database, "track", r => (int)r["id"] == (int)model["track_id"]);
        DataRow location = One(database, "location", r => (int)r["id"] == (int)track["location_id"]);
        var selected = new CircuitCatalog(modelId, (int)track["id"], (int)location["id"]);
        if (selected != new CircuitCatalog(184, 81, 12))
            throw new IOException("The installed layout does not match the private Direct track slot.");
        int race = layout.Circuit ? 2 : 5;                // Rallycross or Raid
        int referenceModel = layout.Circuit ? 143 : 132;  // Battersea Bridge or Baja Peninsula Run
        if (!layout.Circuit)
        {
            var round = One(database, "net_event_default", r => (int)r["race_type"] == race &&
                (int)r["round_index"] == 1 && (int)r["track_model_id"] == referenceModel);
            if ((bool)round["staggered_starts"] || (int)round["number_of_laps"] != 0)
                throw new IOException("The stock Raid reference has unexpected start or lap settings.");
        }
        int[] classes = layout.Circuit ? [17] : [19, 20, 21, 22, 23, 24];
        bool changed = EnsureEntry("net_race_tracks", "net_race_type", race);
        foreach (int vehicleClass in classes)
        {
            _ = One(database, "net_session_restrictions", r =>
                (int)r["net_race_type_id"] == race && (int)r["net_vehicle_class_id"] == vehicleClass);
            changed |= EnsureEntry("net_round_restrictions", "net_vehicle_class_id", vehicleClass);
        }
        if (changed)
        {
            using var output = File.Open(privateDatabase, FileMode.Create, FileAccess.Write);
            database.Write(output);
        }
        return selected;

        bool EnsureEntry(string tableName, string column, int value)
        {
            var table = database.Tables[tableName]!;
            // Require the reference event to exist in this database.
            var reference = One(database, tableName, r => (int)r[column] == value && (int)r["track_model_id"] == referenceModel);
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
