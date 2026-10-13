using System.Text;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.CustomCars;

// This is the inspected F2004 profile key, not a general AC encryption-key generator.
internal static class SourcePhysics
{
    internal static void Extract(string source, string output, CancellationToken cancel)
    {
        byte[] bytes = File.ReadAllBytes(SafeFiles.Inside(source, "data.acd"));
        if (bytes.Length > 32 * 1024 * 1024) throw new IOException("Unsupported physics archive size.");
        using var input = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(input, new UTF8Encoding(false, true));
        if (reader.ReadUInt32() == 0xfffffba9) input.Position = 8; else input.Position = 0;
        byte[] key = Encoding.ASCII.GetBytes("179-44-163-59-166-193-14-53");
        var files = new List<object>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(output);
        while (input.Position < input.Length)
        {
            cancel.ThrowIfCancellationRequested();
            int nameLength = reader.ReadInt32();
            if (nameLength is < 1 or > 180 || nameLength > input.Length - input.Position) throw new IOException("Invalid physics entry name.");
            string name = new UTF8Encoding(false, true).GetString(reader.ReadBytes(nameLength));
            SafeFiles.Relative(name);
            if (name.Contains('/') || !names.Add(name) || names.Count > 256) throw new IOException("Unsupported physics entry.");
            int size = reader.ReadInt32();
            if (size < 0 || size > 8 * 1024 * 1024 || (long)size * 4 > input.Length - input.Position) throw new IOException("Invalid physics entry length.");
            byte[] data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                byte encrypted = reader.ReadByte();
                data[i] = unchecked((byte)(encrypted - key[i % key.Length]));
                if (reader.ReadByte() != 0 || reader.ReadByte() != 0 || reader.ReadByte() != 0) throw new IOException("Unsupported physics encoding.");
                if (unchecked((byte)(data[i] + key[i % key.Length])) != encrypted) throw new IOException("Physics readback failed.");
            }
            string path = SafeFiles.Inside(output, name);
            File.WriteAllBytes(path, data);
            files.Add(new { Name = name, Length = size, Sha256 = SafeFiles.Hash(path, cancel) });
        }
        if (files.Count != 70) throw new IOException("Unsupported F2004 physics inventory.");
        SafeFiles.WriteJson(SafeFiles.Inside(output, "extraction.json"), new { SourceSha256 = SafeFiles.Hash(SafeFiles.Inside(source, "data.acd"), cancel), Files = files });
    }
}
