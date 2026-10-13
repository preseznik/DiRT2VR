using System.Data;
using System.Xml;
using System.Xml.Linq;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Data;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.CustomCars;

public static class CarRegistration
{
    static void RequireCodes(string code, string donor)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z0-9]{3}$") || !System.Text.RegularExpressions.Regex.IsMatch(donor, "^[a-z0-9]{3}$") || code == donor)
            throw new IOException("Invalid car registration identity.");
    }
    public static byte[] Database(byte[] original) => Database(original, CarPack.Code, "prc");
    public static byte[] Database(byte[] original, string code, string donorCode)
    {
        RequireCodes(code, donorCode);
        // EGO's database reader requires filenames. Keep its temporary inputs private.
        string folder = Path.Combine(Path.GetTempPath(), "DiRT2VR-car-db-" + Guid.NewGuid().ToString("N"));
        SafeFiles.NoLinks(folder); Directory.CreateDirectory(folder);
        string input = Path.Combine(folder, "source.bin"), schema = Path.Combine(folder, "schema.xml"), output = Path.Combine(folder, "result.bin");
        try
        {
            File.WriteAllBytes(input, original);
            using (var resource = typeof(CarRegistration).Assembly.GetManifestResourceStream("car-schemaDirt2.xml")!)
            using (var target = File.Create(schema)) resource.CopyTo(target);
            var before = new DatabaseFile(input, schema); var db = new DatabaseFile(input, schema);
            if (db.Tables["vehicle"]!.Rows.Cast<DataRow>().Any(r => (string)r["file_string"] == code)) throw new IOException("The database already contains " + code + ". Restore original files before launching.");
            DataRow One(string table, string column, object value) => db.Tables[table]!.Rows.Cast<DataRow>().Single(r => Equals(r[column], value));
            DataRow Clone(string table, DataRow donor)
            {
                var rows = db.Tables[table]!; var row = rows.NewRow(); row.ItemArray = (object[])donor.ItemArray.Clone();
                row["id"] = checked(rows.Rows.Cast<DataRow>().Max(r => (int)r["id"]) + 1); rows.Rows.Add(row); return row;
            }
            // Some stock codes share a special-event variant. Register from the
            // ordinary handling record, with a stable ID order for equal variants.
            var donor = db.Tables["vehicle"]!.Rows.Cast<DataRow>().Where(r => (string)r["file_string"] == donorCode)
                .OrderBy(r => (bool)r["use_alternative_handling_files"] ? 1 : 0).ThenBy(r => (int)r["id"]).FirstOrDefault()
                ?? throw new IOException("The declared donor car is absent from this database.");
            var model = Clone("vehicle_model", One("vehicle_model", "id", donor["vehicle_model_id"])); model["all_vehicles_achievement"] = false;
            var interior = Clone("vehicle_interior", One("vehicle_interior", "id", donor["vehicle_interior_id"])); interior["file_string"] = code;
            var car = Clone("vehicle", donor);
            car["file_string"] = code; car["vehicle_model_id"] = model["id"]; car["vehicle_interior_id"] = interior["id"];
            car["count_towards_achievement"] = false; car["purchasable"] = false; car["livery_image_to_use"] = code + "_00";
            var livery = Clone("vehicle_livery", db.Tables["vehicle_livery"]!.Rows.Cast<DataRow>().Where(r => Equals(r["vehicle_id"], donor["id"]) && (bool)r["is_default_livery"]).OrderBy(r => (string)r["file_string"] == donorCode + "_00" ? 0 : 1).ThenBy(r => (int)r["id"]).FirstOrDefault() ?? throw new IOException("Donor car has no default livery."));
            livery["vehicle_id"] = car["id"]; livery["file_string"] = code + "_00"; livery["always_allow_online"] = false;
            foreach (var old in db.Tables["vehicle_attribute"]!.Rows.Cast<DataRow>().Where(r => Equals(r["vehicle_id"], donor["id"])).ToArray())
            {
                var row = Clone("vehicle_attribute", old); row["vehicle_id"] = car["id"];
                row["filename"] = ((string)row["filename"]).Replace(donorCode, code, StringComparison.Ordinal);
            }
            using (var stream = File.Create(output)) db.Write(stream);
            var saved = new DatabaseFile(output, schema);
            foreach (DataTable table in db.Tables)
            {
                var actual = saved.Tables[table.TableName]!;
                if (actual.Rows.Count != table.Rows.Count) throw new IOException("Car database roundtrip row count failed.");
                for (int i = 0; i < table.Rows.Count; i++)
                    if (!actual.Rows[i].ItemArray.SequenceEqual(table.Rows[i].ItemArray)) throw new IOException("Car database roundtrip failed.");
                var old = before.Tables[table.TableName]!;
                for (int i = 0; i < old.Rows.Count; i++)
                    if (!actual.Rows[i].ItemArray.SequenceEqual(old.Rows[i].ItemArray)) throw new IOException("Car conversion changed an existing database row.");
            }
            return File.ReadAllBytes(output);
        }
        finally { foreach (var path in new[] { input, schema, output }) if (File.Exists(path)) SafeFiles.DeleteOwned(path); Directory.Delete(folder); }
    }
    public static byte[] Shared(string path, byte[] original) => Shared(path, original, CarPack.Code, "prc", true);
    public static byte[] Shared(string path, byte[] original, string code, string donorCode, bool ferrariAudio = false)
    {
        RequireCodes(code, donorCode);
        if (path == "database/database.bin") return Database(original, code, donorCode);
        if (path == "cars/class_mappings.xml")
        {
            using var input = new MemoryStream(original, false); var document = XDocument.Load(input);
            var mappings = document.Root!.Element("class_mappings")!;
            if (mappings.Elements("mapping").Any(m => (string?)m.Attribute("car") == code)) throw new IOException(code + " mapping already exists.");
            var donor = mappings.Elements("mapping").Single(m => (string?)m.Attribute("car") == donorCode);
            mappings.Add(new XElement("mapping", new XAttribute("car", code), new XAttribute("class", (string)donor.Attribute("class")!)));
            using var output = new MemoryStream(); document.Save(output); return output.ToArray();
        }
        using var stream = new MemoryStream(original, false); var binary = new XmlFile(stream); var xml = binary.Document;
        if (path == "audio/dictionaries.bin")
        {
            if (!ferrariAudio) return original; // External cars reuse the installed donor audio.
            var dictionaries = xml.SelectSingleNode("/Dictionaries") ?? throw new IOException("Missing native dictionaries.");
            if (dictionaries.SelectSingleNode("Dictionary[@path='audio/dictionaries/d2vr_f04/']") is not null) throw new IOException("f04 dictionary already exists.");
            var item = xml.CreateElement("Dictionary"); item.SetAttribute("path", "audio/dictionaries/d2vr_f04/"); dictionaries.AppendChild(item);
        }
        else if (path is "audio/carsetup.bin" or "audio/carsetup_ai.bin")
        {
            var cars = xml.SelectSingleNode("/CarSetup/Cars") ?? throw new IOException("Missing native audio cars.");
            bool ai = path.EndsWith("_ai.bin", StringComparison.Ordinal); string audioCode = code + (ai ? "_ai" : "");
            if (cars.SelectSingleNode($"Car[@name='{audioCode}']") is not null) throw new IOException(code + " audio already exists.");
            var car = (XmlElement)(cars.SelectSingleNode($"Car[@name='{donorCode}{(ai ? "_ai" : "")}']")?.CloneNode(true) ?? throw new IOException("Missing donor audio."));
            car.SetAttribute("name", audioCode);
            if (!ai && ferrariAudio)
            {
                car.SetAttribute("openCockpit", "true");
                ((XmlElement)car.SelectSingleNode("EngineIniFile")!).SetAttribute("value", CarPack.EngineIni);
                var engine = (XmlElement)car.SelectSingleNode("EngineBank")!; engine.SetAttribute("value", "eng_f04"); engine.SetAttribute("egoEngineSound", "0");
                engine.SetAttribute("throttleLength", "320000"); engine.SetAttribute("idleLength", "320000");
                var mechanical = (XmlElement)car.SelectSingleNode("SplitMech")!; mechanical.SetAttribute("Clunk", "mec_f04_shift"); mechanical.SetAttribute("Exhaust", "mec_f04_exh");
                var trims = (XmlElement)car.SelectSingleNode("VolumeTrims")!; trims.SetAttribute("engine", "0.75"); trims.SetAttribute("exhaust", "1.15"); trims.SetAttribute("WhineSpeedTrim", "-7.5"); trims.SetAttribute("WhineRPMTrim", "-8.5");
            }
            cars.AppendChild(car);
        }
        else throw new IOException("Unsupported custom-car shared target.");
        using var result = new MemoryStream(); binary.Write(result, binary.Type);
        using var checkInput = new MemoryStream(result.ToArray(), false); var check = new XmlFile(checkInput);
        if (xml.DocumentElement!.OuterXml != check.Document.DocumentElement!.OuterXml) throw new IOException("Car audio readback failed.");
        return result.ToArray();
    }
}
