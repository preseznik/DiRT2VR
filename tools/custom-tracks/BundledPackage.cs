using System.IO.Compression;

namespace DiRT2VR.CustomTracks;

public sealed record TrackProgress(int Percent, string Message);

public static class BundledPackage
{
    public const string Manifest = "aspen-package.json";
    public static PackageOffer Read(string folder, string launcherVersion, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        string path = SafeFiles.Inside(folder, Manifest);
        if (!File.Exists(path)) throw new IOException("Aspen conversion tools are missing. Reinstall DiRT2VR or extract the complete ZIP.");
        var catalog = SafeFiles.ReadJson<Catalog>(path);
        if (catalog.Schema != 1 || catalog.Tracks is null || catalog.Tracks.Length != 1)
            throw new IOException("Invalid bundled Aspen metadata. Reinstall DiRT2VR.");
        var offer = catalog.Tracks[0]; AspenPack.Validate(offer, launcherVersion); return offer;
    }
    public static void CopyArchive(string folder, PackageOffer offer, string destination, string launcherVersion, CancellationToken cancel)
    {
        AspenPack.Validate(offer, launcherVersion); SafeFiles.NoLinks(destination);
        string source = SafeFiles.Inside(folder, offer.ArchiveName);
        if (!File.Exists(source) || new FileInfo(source).Length != offer.ArchiveBytes)
            throw new IOException("Aspen conversion tools are missing or damaged. Reinstall DiRT2VR or extract the complete ZIP.");
        using var input = File.OpenRead(source);
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            BoundedCopy(input, output, offer.ArchiveBytes, cancel);
            if (output.Length != offer.ArchiveBytes) throw new IOException("Bundled Aspen tools are incomplete. Reinstall DiRT2VR.");
            output.Flush(true);
        }
        cancel.ThrowIfCancellationRequested();
        if (!SafeFiles.Hash(destination).Equals(offer.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Bundled Aspen tools failed verification. Reinstall DiRT2VR.");
    }
    static void BoundedCopy(Stream input, Stream output, long limit, CancellationToken cancel)
    {
        byte[] buffer = new byte[131072]; long total = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            int read = input.Read(buffer);
            if (read == 0) break;
            total = checked(total + read);
            if (total > limit) throw new IOException("Package exceeds the expected size.");
            output.Write(buffer, 0, read);
        }
    }
    public static void Extract(string zip, string output, CancellationToken cancel)
    {
        SafeFiles.NoLinks(zip); SafeFiles.NoLinks(output);
        if (Directory.Exists(output)) throw new IOException("Extraction directory must be new.");
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count is < 1 or > 512) throw new IOException("Invalid package file count.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            string name = entry.FullName;
            SafeFiles.Relative(name);
            if (!paths.Add(name) || (entry.ExternalAttributes & 0x400) != 0 || ((entry.ExternalAttributes >> 16) & 0xF000) is 0xA000 or 0x4000 ||
                !(name == "AspenConverter.exe" || name == "README.txt" || name == "package.json" ||
                    (name.StartsWith("licenses/", StringComparison.Ordinal) && new[] { ".txt", ".md" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))) ||
                entry.Length < 0 || entry.Length > 512L * 1024 * 1024)
                throw new IOException("Unsupported file or link in conversion package: " + name);
            total = checked(total + entry.Length);
        }
        if (!paths.Contains("AspenConverter.exe") || total > 768L * 1024 * 1024) throw new IOException("Incomplete or oversized conversion package.");
        Directory.CreateDirectory(output);
        foreach (var entry in archive.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            string target = SafeFiles.Inside(output, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var file = new FileStream(target, FileMode.CreateNew);
            BoundedCopy(input, file, entry.Length, cancel);
            if (file.Length != entry.Length) throw new IOException("Incomplete package file.");
        }
    }
    public static void VerifySources(PackageOffer offer, string dirt2, string dirt3, IProgress<TrackProgress>? progress, CancellationToken cancel)
    {
        AspenPack.ValidateSources(offer.Sources);
        for (int i = 0; i < offer.Sources.Length; i++)
        {
            cancel.ThrowIfCancellationRequested(); var source = offer.Sources[i];
            string path = SafeFiles.Inside(source.Game == "dirt2" ? dirt2 : dirt3, source.Path);
            if (!File.Exists(path) || SafeFiles.Hash(path) != source.Sha256)
                throw new IOException($"Missing or unsupported {source.Game} source file: {source.Path}. Choose the correct game folder or verify the original files in Steam.");
            progress?.Report(new((i + 1) * 100 / offer.Sources.Length, "Checking original game files"));
        }
    }
}
