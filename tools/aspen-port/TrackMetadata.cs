using System.Data;
using EgoEngineLibrary.Data;
using EgoEngineLibrary.Language;

public sealed record TrackIdentity(string DirectoryName, string StringId, string DisplayName, string LocationKey, string LocationName, int DonorModel);

// Shared database/localization transaction builder; venue-specific geometry stays separate.
public static class TrackMetadata
{
    public static void Create(string game, string schema, string output, TrackIdentity identity, float length, bool? night = null, Dictionary<string,string>? inputs = null)
    {
        if (!float.IsFinite(length) || length <= 0) throw new InvalidDataException("Invalid circuit length.");
        PortFiles.NewOutput(output, game);
        var source = Path.Combine(game, "database/database.bin");
        PortFiles.NoLinks(source); PortFiles.NoLinks(schema);
        var original = new DatabaseFile(source, schema);
        var database = new DatabaseFile(source, schema);
        DataRow Clone(string table, int donor)
        {
            var t = database.Tables[table]!;
            var row = t.NewRow(); row.ItemArray = t.Rows.Cast<DataRow>().Single(r => (int)r["id"] == donor).ItemArray;
            row["id"] = t.Rows.Cast<DataRow>().Max(r => (int)r["id"]) + 1;
            t.Rows.Add(row); return row;
        }
        if (database.Tables["track_model"]!.Rows.Cast<DataRow>().Any(r => (string)r["file_string"] == identity.DirectoryName))
            throw new InvalidDataException("Source database already contains the custom track.");
        var location = Clone("location", 8); // Utah: US region defaults; independent identity.
        location["name_string_id"] = identity.LocationKey; location["count_for_achievement"] = false;
        var track = Clone("track", 61);
        track["location_id"] = location["id"]; track["name_string_id"] = identity.LocationKey;
        track["nationality_id"] = 2; track["count_towards_stats"] = false;
        var model = Clone("track_model", identity.DonorModel);
        model["file_string"] = identity.DirectoryName; model["folder_string"] = "usa"; model["route_string"] = "route_0";
        model["track_id"] = track["id"]; model["name_string_id"] = identity.StringId;
        model["include_in_cycle_tracks"] = false; model["every_track_achievement"] = false;
        model["skipfe_track"] = false;
        model["length"] = length;
        if (night.HasValue) {
            model["headlights_on"] = night.Value;
            model["track_lights_on"] = night.Value;
        }
        var target = Path.Combine(output, "database/database.bin"); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using (var stream = new FileStream(target, FileMode.CreateNew)) database.Write(stream);
        var saved = new DatabaseFile(target, schema);
        foreach (DataTable table in database.Tables)
            for(int i=0;i<table.Rows.Count;i++)
                if(!table.Rows[i].ItemArray.SequenceEqual(saved.Tables[table.TableName]!.Rows[i].ItemArray)) throw new InvalidDataException("Database serialization changed metadata.");
        foreach (DataTable table in original.Tables)
        {
            var after = saved.Tables[table.TableName]!;
            int added = table.TableName is "location" or "track" or "track_model" ? 1 : 0;
            if (after.Rows.Count != table.Rows.Count + added) throw new InvalidDataException("Unexpected metadata row count.");
            for (int i = 0; i < table.Rows.Count; i++)
                if (!table.Rows[i].ItemArray.SequenceEqual(after.Rows[i].ItemArray)) throw new InvalidDataException("Stock database row changed.");
        }
        foreach (var path in Directory.EnumerateFiles(Path.Combine(game, "language"), "language_*.lng").Order())
        {
            var language = new LngFile(PortFiles.OpenRead(path));
            var before = language.hsht.Entries.Select(e => (e.Key,e.Value)).Order().ToArray();
            string key = "db_" + identity.StringId;
            if (before.Any(e => e.Key == "db_" + identity.LocationKey || e.Key == key)) throw new InvalidDataException("Source language already contains the custom track.");
            language.Add("db_" + identity.LocationKey, identity.LocationName, false);
            language.Add(key, identity.DisplayName, false);
            var destination = Path.Combine(output, "language", Path.GetFileName(path)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            language.Write(new FileStream(destination, FileMode.CreateNew));
            var after = new LngFile(PortFiles.OpenRead(destination)).hsht.Entries.Select(e => (e.Key,e.Value)).Order().ToArray();
            if (after.Length != before.Length + 2 || !before.SequenceEqual(after.Where(e => e.Key != "db_" + identity.LocationKey && e.Key != key)))
                throw new InvalidDataException("Stock localization changed.");
        }
        var entries = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Order().Select(p => {
            string relative = Path.GetRelativePath(output, p).Replace('\\','/');
            var from = Path.Combine(game, relative); var hash = PortFiles.Hash(from);
            if (inputs is not null) inputs[from] = hash;
            return new MetadataTransaction.Entry(relative, hash, PortFiles.Hash(p));
        }).ToArray();
        PortFiles.Json(Path.Combine(output, "metadata.json"), new MetadataTransaction.Manifest(1, entries));
        PortFiles.Json(Path.Combine(output, "identity.json"), new { LocationId=location["id"], TrackId=track["id"], TrackModelId=model["id"], StockRowsPreserved=true });
    }
}
