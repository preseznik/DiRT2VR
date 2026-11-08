using System.Data;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Data;

namespace DiRT2VR.Multiplayer;

internal sealed record CircuitCatalog(int Model, int Track, int Location)
{
    // Register the authored circuit for the same Rallycross event and car
    // class as Battersea. The shared session rules and stock routes stay intact.
    internal static CircuitCatalog Register(string privateDatabase, string schema, Layout layout)
    {
        using var database = new DatabaseFile(privateDatabase, schema);
        DataRow model = One(database, "track_model", r => (string)r["folder_string"] == "usa" &&
            (string)r["file_string"] == layout.Folder && (string)r["route_string"] == "route_0");
        int modelId = (int)model["id"];
        DataRow track = One(database, "track", r => (int)r["id"] == (int)model["track_id"]);
        DataRow location = One(database, "location", r => (int)r["id"] == (int)track["location_id"]);
        var selected = new CircuitCatalog(modelId, (int)track["id"], (int)location["id"]);
        if (layout.Id != DirectLayout.SpaId || selected != new CircuitCatalog(184, 81, 12))
            throw new IOException("The installed circuit does not match the Spa route supported by this Direct build.");
        _ = One(database, "net_session_restrictions", r =>
            (int)r["net_race_type_id"] == 2 && (int)r["net_vehicle_class_id"] == 17);
        bool raceAdded = EnsureEntry("net_race_tracks", "net_race_type", 2);
        bool classAdded = EnsureEntry("net_round_restrictions", "net_vehicle_class_id", 17);
        if (raceAdded || classAdded)
        {
            using var output = File.Open(privateDatabase, FileMode.Create, FileAccess.Write);
            database.Write(output);
        }
        return selected;

        bool EnsureEntry(string tableName, string column, int value)
        {
            var table = database.Tables[tableName]!;
            // Require the reference event to exist in this database.
            var reference = One(database, tableName, r => (int)r[column] == value && (int)r["track_model_id"] == 143);
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
