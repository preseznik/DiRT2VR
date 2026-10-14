using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using DiRT2VR.Nordschleife;

// Bounded reader for the fingerprinted GRID 2 Spa DLC, NeFS 1.6.
// Format/public RSA key: github.com/EgoEngineModding/ego.nefsedit (MIT).
internal sealed class SpaArchive
{
    internal const string RelativePath = "download/spa_francorchamps/spa_francorchamps.nefs";
    internal const string ExpectedHash = "AE4C5E528A0DAE12A8B5C8684B7EE870359C7A3EF12BD08B779D3A7CC56C450C";
    const int FileLimit = 256 << 20;
    const string Modulus = "D9AEBEE86023C6CEF41FC490A8AECCD7A96636B29C02A5EA4B1590FCFF01894D573D8F603F54DE61E1A268980B803A285669CB7B9FDD086DA3846B8251915D60F92C86B202199C05A8284FFFCA3C60E75CA184D56FA83470F7F40D847883BBD64230881F7A3B600F3598ADF01C4690CBA846443FB3E0E68E64C54A447253BBAF";
    sealed record Entry(int Start, uint Block, uint Parent, string Name, int Size, uint Id, ushort Flags);
    readonly byte[] data, header;
    readonly Entry[] entries;
    static int Int(byte[] b, int p) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4)));
    static uint UInt(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
    static ushort Short(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p, 2));
    static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    static byte[] Decrypt(ReadOnlySpan<byte> bytes, byte[] intro)
    {
        using var aes = Aes.Create();
        aes.Key = Convert.FromHexString(Encoding.ASCII.GetString(intro, 36, 64));
        return aes.DecryptEcb(bytes, PaddingMode.None);
    }
    SpaArchive(byte[] bytes)
    {
        data = bytes; header = bytes;
        if (!bytes.AsSpan(0, 4).SequenceEqual("NeFS"u8))
        {
            var value = BigInteger.ModPow(new BigInteger(bytes.AsSpan(0, 128), isUnsigned: true), 65537,
                new BigInteger(Convert.FromHexString(Modulus), isUnsigned: true));
            var intro = new byte[128];
            if (!value.TryWriteBytes(intro, out _, isUnsigned: true) || !intro.AsSpan(0, 4).SequenceEqual("NeFS"u8))
                throw new InvalidDataException("Invalid Spa archive signature.");
            int end = Int(intro, 100);
            if (end < 160 || end > bytes.Length || end > 16 << 20) throw new InvalidDataException("Invalid Spa archive header size.");
            header = [..intro, ..Decrypt(bytes.AsSpan(128, end - 128), intro)];
        }
        if (Int(header, 104) != 0x10600 || Short(header, 128) != 1) throw new InvalidDataException("Unsupported Spa archive version.");
        int count = Int(header, 108);
        if (count is < 1 or > 4096) throw new InvalidDataException("Invalid Spa archive inventory.");
        entries = new Entry[count];
        for (int i = 0; i < count; i++)
        {
            int p = checked(Int(header, 136) + i * 20), s = checked(Int(header, 144) + Int(header, p + 8) * 20);
            int n = checked(Int(header, 152) + Int(header, s + 8)), end = Array.IndexOf(header, (byte)0, n);
            if (end < n || end - n > 1024) throw new InvalidDataException("Invalid Spa archive filename.");
            entries[i] = new(checked((int)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(p, 8))), UInt(header, p + 12),
                UInt(header, s), Encoding.UTF8.GetString(header, n, end - n), Int(header, s + 12), UInt(header, s + 16), Short(header, checked(Int(header, 140) + i * 4 + 2)));
        }
        if (entries.DistinctBy(e => e.Id).Count() != count) throw new InvalidDataException("Duplicate Spa archive identity.");
    }
    string Name(Entry entry, HashSet<uint>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(entry.Id) || seen.Count > 32) throw new InvalidDataException("Invalid Spa archive directory tree.");
        var parent = entries.SingleOrDefault(e => e.Id == entry.Parent);
        return parent is not null && parent.Id != entry.Id ? Name(parent, seen) + "/" + entry.Name : entry.Name;
    }
    static byte[] Inflate(Stream input, int limit, bool gzip)
    {
        using Stream decoder = gzip ? new GZipStream(input, CompressionMode.Decompress) : new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[65536]; int count;
        while ((count = decoder.Read(buffer)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Spa archive expands beyond its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    byte[] Read(Entry entry, int limit)
    {
        if ((entry.Flags & 2) != 0 || entry.Size > limit) throw new InvalidDataException("Unsupported Spa archive member.");
        if (entry.Block == uint.MaxValue) return data.AsSpan(entry.Start, entry.Size).ToArray();
        int blockSize = checked(Short(header, 132) * 32768);
        if (blockSize <= 0) throw new InvalidDataException("Invalid Spa archive block size.");
        using var output = new MemoryStream(entry.Size);
        int previous = 0, left = entry.Size;
        for (int j = 0; left > 0; j++)
        {
            int p = checked(Int(header, 156) + (checked((int)entry.Block) + j) * 8), end = Int(header, p), count = Math.Min(blockSize, left);
            if (end < previous) throw new InvalidDataException("Invalid Spa archive block extent.");
            var block = data.AsSpan(checked(entry.Start + previous), end - previous).ToArray();
            switch (Short(header, p + 4))
            {
                case 0: break;
                case 4: block = Decrypt(block, header).AsSpan(0, count).ToArray(); break;
                case 7: using (var input = new MemoryStream(block)) block = Inflate(input, count, false); break;
                default: throw new InvalidDataException("Unsupported Spa archive compression.");
            }
            if (block.Length != count) throw new InvalidDataException("Spa archive block length mismatch.");
            output.Write(block); left -= count; previous = end;
        }
        return output.ToArray();
    }
    internal static void Extract(string source, string output)
    {
        Files.NoLinks(source);
        if (new FileInfo(source).Length > 1L << 30 || Files.Hash(source) != ExpectedHash) throw new InvalidDataException("Unsupported GRID 2 Spa DLC. Verify its files in Steam.");
        Files.NewOutput(output, source);
        var outer = new SpaArchive(File.ReadAllBytes(source));
        var venue = new SpaArchive(outer.Read(outer.entries.Single(e => e.Name == "europe_spa_francorchamps.nefs"), 1 << 30));
        var selected = venue.entries.Where(e => (e.Flags & 2) == 0).ToArray();
        if (selected.Length != 179) throw new InvalidDataException("Unexpected Spa DLC inventory.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<object>(); long total = 0;
        foreach (var entry in selected)
        {
            string name = venue.Name(entry);
            string[] shared = ["patches/tracks/europe_spa_francorchamps", "tracks/generic/vegetationdeform.xml", "audio/soundbanks/amb_spa_francorchamps.bnk", "audio/soundbanks/props_spa_francorchamps.bnk"];
            if ((!name.StartsWith("tracks/locations/europe/spa_francorchamps/", StringComparison.Ordinal) && !shared.Contains(name)) || !names.Add(name))
                throw new InvalidDataException("Unexpected Spa DLC path: " + name);
            string target = Files.Inside(output, name);
            byte[] original = venue.Read(entry, FileLimit), bytes = original;
            bool gzip = original.Length >= 2 && original[0] == 0x1f && original[1] == 0x8b;
            if (gzip) using (var input = new MemoryStream(original)) bytes = Inflate(input, FileLimit, true);
            total += bytes.Length;
            if (total > 2L << 30) throw new InvalidDataException("Spa DLC exceeds the extraction budget.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var stream = new FileStream(target, FileMode.CreateNew)) stream.Write(bytes);
            files.Add(new { Path = name, ArchiveBytes = original.Length, ArchiveSha256 = Hash(original), StagedBytes = bytes.Length, StagedSha256 = Hash(bytes), GzipDecoded = gzip });
        }
        if (Files.Hash(source) != ExpectedHash) throw new InvalidDataException("Spa DLC changed during extraction.");
        Files.Json(Path.Combine(output, "source.json"), new { Source = source, Sha256 = ExpectedHash, VenueFiles = files.Count, Files = files });
    }
}
