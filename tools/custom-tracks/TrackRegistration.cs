using System.Data;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EgoEngineLibrary.Data;
using EgoEngineLibrary.Language;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.CustomTracks;

// An External layout registered in the player's own game files for the session instead of shipping modified copies:
// a location, track and track model cloned from stock donor rows (with the donor model's world records and AI split
// times scaled by RecordScale), the location and model names in every language file, and optionally a tree budget
// style cloned from a stock one. Stock rows and strings are never changed.
public sealed record ExternalRegistration(int LocationDonor, string LocationKey, string LocationName, int TrackDonor, int Nationality,
    int ModelDonor, string ModelKey, string ModelName, float Length, float RecordScale,
    string? TreeStyle = null, string TreeBase = "test", int TreeInstances = 0, int TreeBillboards = 0);

public static class TrackRegistration
{
    public const string Database = "database/database.bin", TreeSettings = "tracks/tree_system_settings.xml";
    public static readonly string[] Languages = ["eng", "fre", "ger", "ita", "jpn", "pol", "rus", "spa", "use"];
    public static string[] Targets(ExternalRegistration r) =>
        [Database, .. Languages.Select(l => $"language/language_{l}.lng"), .. r.TreeStyle is null ? Array.Empty<string>() : [TreeSettings]];

    static bool Key(string? s) => s is not null && Regex.IsMatch(s, "^[a-z0-9_]{1,24}$");
    static bool Text(string? s) => s is not null && !string.IsNullOrWhiteSpace(s) && s.Length <= 60 && !s.Any(char.IsControl);
    public static void Validate(ExternalRegistration? r)
    {
        if (r is null || r.LocationDonor < 1 || r.TrackDonor < 1 || r.ModelDonor < 1 || r.Nationality is < 0 or > 64 ||
            !Key(r.LocationKey) || !Key(r.ModelKey) || r.LocationKey == r.ModelKey || !Text(r.LocationName) || !Text(r.ModelName) ||
            !float.IsFinite(r.Length) || r.Length is < 100 or > 100000 || !float.IsFinite(r.RecordScale) || r.RecordScale is < 0.01f or > 100 ||
            (r.TreeStyle is not null && (!Key(r.TreeStyle) || !Key(r.TreeBase) || r.TreeStyle == r.TreeBase ||
                r.TreeInstances is < 1 or > 12000 || r.TreeBillboards is < 1 or > 12000)))
            throw new IOException("Invalid external track registration.");
    }

    // The registered version of one shared target, from the player's own (pinned) original bytes.
    public static byte[] Shared(string target, byte[] original, ExternalRegistration r, string folder)
    {
        Validate(r);
        if (target == Database) return RegisterDatabase(original, r, folder);
        if (target == TreeSettings && r.TreeStyle is not null) return AddTreeStyle(original, r);
        if (Languages.Any(l => target == $"language/language_{l}.lng")) return AddNames(original, r);
        throw new IOException("Unsupported external registration target: " + target);
    }

    static byte[] RegisterDatabase(byte[] original, ExternalRegistration r, string folder)
    {
        // EGO's database reader requires filenames. Keep its temporary inputs private.
        string work = Path.Combine(Path.GetTempPath(), "DiRT2VR-track-db-" + Guid.NewGuid().ToString("N"));
        SafeFiles.NoLinks(work); Directory.CreateDirectory(work);
        string input = Path.Combine(work, "source.bin"), schema = Path.Combine(work, "schema.xml"), output = Path.Combine(work, "result.bin");
        try
        {
            File.WriteAllBytes(input, original);
            using (var resource = typeof(TrackRegistration).Assembly.GetManifestResourceStream("track-schemaDirt2.xml")!)
            using (var target = File.Create(schema)) resource.CopyTo(target);
            var before = new DatabaseFile(input, schema); var db = new DatabaseFile(input, schema);
            DataRow Find(string table, int id) => db.Tables[table]!.Rows.Cast<DataRow>().SingleOrDefault(row => (int)row["id"] == id)
                ?? throw new IOException($"The registration donor {table} {id} is absent from this database.");
            DataRow Clone(string table, DataRow donor)
            {
                var rows = db.Tables[table]!; var row = rows.NewRow(); row.ItemArray = (object[])donor.ItemArray.Clone();
                row["id"] = checked(rows.Rows.Cast<DataRow>().Max(x => (int)x["id"]) + 1); rows.Rows.Add(row); return row;
            }
            if (db.Tables["track_model"]!.Rows.Cast<DataRow>().Any(row => (string)row["file_string"] == folder))
                throw new IOException("The database already registers " + folder + ". Restore original files before launching.");
            var location = Clone("location", Find("location", r.LocationDonor));
            location["name_string_id"] = r.LocationKey; location["count_for_achievement"] = false;
            var track = Clone("track", Find("track", r.TrackDonor));
            track["location_id"] = location["id"]; track["name_string_id"] = r.LocationKey; track["nationality_id"] = r.Nationality; track["count_towards_stats"] = false;
            var donorModel = Find("track_model", r.ModelDonor);
            var model = Clone("track_model", donorModel);
            model["file_string"] = folder; model["folder_string"] = "usa"; model["route_string"] = "route_0"; model["track_id"] = track["id"]; model["name_string_id"] = r.ModelKey;
            model["include_in_cycle_tracks"] = false; model["every_track_achievement"] = false; model["skipfe_track"] = false; model["length"] = r.Length;
            // World records and AI split times per vehicle class: the donor model's, scaled for the new route.
            foreach (string table in new[] { "world_record", "virtual_times_standard" })
            {
                var rows = db.Tables[table]!;
                foreach (var donor in rows.Rows.Cast<DataRow>().Where(row => Equals(row["track_model_id"], donorModel["id"])).ToArray())
                {
                    var row = Clone(table, donor); row["track_model_id"] = model["id"];
                    foreach (DataColumn c in rows.Columns)
                        if (c.DataType == typeof(float) && (c.ColumnName == "world_record" || c.ColumnName.Contains("split", StringComparison.Ordinal))) row[c] = (float)row[c] * r.RecordScale;
                }
            }
            using (var stream = File.Create(output)) db.Write(stream);
            var saved = new DatabaseFile(output, schema);
            foreach (DataTable table in db.Tables)
            {
                var actual = saved.Tables[table.TableName]!; var old = before.Tables[table.TableName]!;
                if (actual.Rows.Count != table.Rows.Count) throw new IOException("Track registration roundtrip row count failed.");
                for (int i = 0; i < table.Rows.Count; i++)
                    if (!actual.Rows[i].ItemArray.SequenceEqual(table.Rows[i].ItemArray)) throw new IOException("Track registration roundtrip failed.");
                for (int i = 0; i < old.Rows.Count; i++)
                    if (!actual.Rows[i].ItemArray.SequenceEqual(old.Rows[i].ItemArray)) throw new IOException("Track registration changed an existing database row.");
            }
            return File.ReadAllBytes(output);
        }
        finally { foreach (var path in new[] { input, schema, output }) if (File.Exists(path)) SafeFiles.DeleteOwned(path); Directory.Delete(work); }
    }

    static byte[] AddNames(byte[] original, ExternalRegistration r)
    {
        LngFile language; using (var input = new MemoryStream(original, false)) language = new LngFile(input);
        foreach (var (key, value) in new[] { ("db_" + r.LocationKey, r.LocationName), ("db_" + r.ModelKey, r.ModelName) })
        {
            if (language.ContainsKey(key)) throw new IOException("The language file already contains " + key + ". Restore original files before launching.");
            language.Add(key, value, false);
        }
        using var output = new MemoryStream(); language.Write(output);
        var bytes = output.ToArray();
        LngFile check; using (var readback = new MemoryStream(bytes, false)) check = new LngFile(readback);
        if (check["db_" + r.LocationKey] != r.LocationName || check["db_" + r.ModelKey] != r.ModelName) throw new IOException("Track name readback failed.");
        return bytes;
    }

    static byte[] AddTreeStyle(byte[] original, ExternalRegistration r)
    {
        XmlFile source; using (var input = new MemoryStream(original, false)) source = new XmlFile(input);
        var document = XDocument.Parse(source.Document.OuterXml);
        var styles = document.Root!.Elements("setting_style").ToArray();
        if (styles.Any(e => (string?)e.Attribute("name") == r.TreeStyle)) throw new IOException("Tree settings already define " + r.TreeStyle + ". Restore original files before launching.");
        var style = new XElement(styles.SingleOrDefault(e => (string?)e.Attribute("name") == r.TreeBase) ?? throw new IOException("The tree settings have no " + r.TreeBase + " style."));
        style.SetAttributeValue("name", r.TreeStyle);
        foreach (var traversal in style.Elements())
        {
            string? name = (string?)traversal.Attribute("name");
            if (name == "main_scene") { traversal.SetAttributeValue("instance_buffer", r.TreeInstances); traversal.SetAttributeValue("billboard_buffer", r.TreeBillboards); }
            if (name == "shadow_2") traversal.SetAttributeValue("billboard_buffer", r.TreeBillboards);
        }
        document.Root.Add(style);
        using var memory = new MemoryStream(); document.Save(memory); memory.Position = 0;
        using var output = new MemoryStream(); new XmlFile(memory).Write(output, source.Type);
        return output.ToArray();
    }
}
