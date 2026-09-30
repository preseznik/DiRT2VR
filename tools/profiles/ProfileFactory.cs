using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace DiRT2VR.Profiles;
internal static class ProfileFactory
{
    internal static readonly string[] Files = ["AGGRIC CZVKEKG WOM MIWC", "BRR_YYDEBS", "CBTAXDHUDKR",
        "DXXRROKXHXWYTPTIC", "GTEHXOSJZ", "KOHFMZFI", "LK_AIE_FQQJC_RBHP_CZVKEKG", "LKI_RPOI_KKANT",
        "LKIEENWUUMXRY", "LKIFEGWTCIDY", "LKIFEQWJXZKROCT", "NXDSMWW", "PBXAXPJYNZWYTPTIC",
        "PGRRLTKJNZI", "PKLNVOK_RIFC", "QEHGIXXYKM", "STABGVK", "TKWVGWWIDBDGTVFQLFQFMB",
        "TKWVGWWIDBENYPIIOVQSI", "TOSRSADQXMNKGCNKPJ", "TUXPINGKMBOPY"];

    internal static Dictionary<string, byte[]> Build(string name, bool completed)
    {
        using var source = typeof(ProfileFactory).Assembly.GetManifestResourceStream("DiRT2VR.Profile." + (completed ? "Completed" : "Fresh"))
            ?? throw new IOException("Profile defaults are missing.");
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        if (archive.Entries.Count != Files.Length) throw new InvalidDataException("Profile defaults are incomplete.");
        var records = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!Files.Contains(entry.FullName) || entry.Length is < 1 or > 262144 || records.ContainsKey(entry.FullName))
                throw new InvalidDataException("Invalid profile defaults.");
            using var stream = entry.Open();
            var bytes = new byte[checked((int)entry.Length)]; stream.ReadExactly(bytes);
            records.Add(entry.FullName, bytes);
        }
        uint identity = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        uint user = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var xml = Dirt2SaveContainer.Decode(Dirt2SaveContainer.CreateRecord(records["NXDSMWW"], identity)).ReadProfileXml();
        var parts = name.Split(' ', 2);
        xml.Root!.Element("Player_FirstName")!.Value = parts[0];
        xml.Root.Element("Player_LastName")!.Value = parts.Length == 2 ? parts[1] : "";
        xml.Root.Element("profile_id")!.Value = identity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        xml.Root.Element("savegame_user_id")!.Value = user.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var encodedXml = new MemoryStream();
        using (var writer = XmlWriter.Create(encodedXml, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false })) xml.Save(writer);
        var xmlBytes = encodedXml.ToArray();
        var profile = new byte[4 + xmlBytes.Length + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(profile, checked((uint)xmlBytes.Length + 1));
        xmlBytes.CopyTo(profile, 4); records["NXDSMWW"] = profile;
        var summary = records["QEHGIXXYKM"];
        int textLength = 0;
        for (int i=0;i<4;i++) textLength += BinaryPrimitives.ReadUInt16LittleEndian(summary.AsSpan(i*2));
        if (summary.Length != 12+textLength+24) throw new InvalidDataException("Invalid profile summary defaults.");
        var tail = summary.AsSpan(12+textLength).ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(tail.AsSpan(16), user);
        byte[][] strings = [Encoding.UTF8.GetBytes("Autosave0"), Encoding.UTF8.GetBytes("Colin McRae: DiRT 2"),
            Encoding.UTF8.GetBytes(name), Encoding.UTF8.GetBytes(completed ? "DiRT Tour XP: 665500" : "DiRT Tour XP: 0")];
        using var output = new MemoryStream(); using var binary = new BinaryWriter(output, Encoding.UTF8, true);
        foreach (var text in strings) binary.Write(checked((ushort)text.Length));
        binary.Write(BinaryPrimitives.ReadUInt32LittleEndian(summary.AsSpan(8)));
        foreach (var text in strings) binary.Write(text);
        binary.Write(tail); records["QEHGIXXYKM"] = output.ToArray();
        return records.ToDictionary(pair => pair.Key, pair => Dirt2SaveContainer.CreateRecord(pair.Value, identity));
    }
}
