using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.CustomCars;

public static class PackExport
{
    // Inventory only: never include profiles, preferences, journals or unlisted files.
    public static void Write(string game, string kind, string id, string destination,
        string launcherVersion, Action<TrackProgress>? progress = null, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        destination = Path.GetFullPath(destination);
        SafeFiles.NoLinks(destination);
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a .zip file for the exported pack.");
        string receiptPath;
        byte[] receiptBytes;
        PackFile[] files;
        if (kind == "track" && TrackPacks.Get(id).External is null)
        {
            var pack = TrackPacks.Get(id);
            receiptPath = pack.Receipt;
            receiptBytes = ReadReceipt(game, receiptPath);
            var receipt = JsonSerializer.Deserialize<PackReceipt>(receiptBytes, SafeFiles.Json)!;
            pack.Validate(receipt);
            if (Version.Parse(receipt.MinimumLauncher) > Version.Parse(launcherVersion))
                throw new IOException("Update the launcher before exporting this track pack.");
            files = receipt.Files;
        }
        else if (kind == "car" && id == CarPack.Code)
        {
            receiptPath = CarPack.Receipt;
            receiptBytes = ReadReceipt(game, receiptPath);
            var receipt = JsonSerializer.Deserialize<CarReceipt>(receiptBytes, SafeFiles.Json)!;
            CarPack.Validate(receipt);
            files = receipt.Files;
        }
        else
        {
            if (kind is not ("track" or "car")) throw new IOException("Unknown pack kind.");
            var pack = TrackPacks.External.SingleOrDefault(p => p.Kind == kind && p.Id == id)
                ?? throw new IOException("The external pack is missing. Refresh the content list.");
            if (!pack.Ready) throw new IOException(pack.Problem);
            receiptPath = pack.Receipt;
            receiptBytes = ReadReceipt(game, receiptPath);
            if (Convert.ToHexString(SHA256.HashData(receiptBytes)) != pack.ManifestHash)
                throw new IOException("The external manifest changed. Refresh the content list.");
            files = pack.Manifest!.Files.Select(f => new PackFile(pack.Support + "/" + f.Path, f.Bytes, f.Sha256)).ToArray();
        }
        // Store the original receipt/manifest verbatim, preserving pack/replay identity.
        var inventory = files.Append(new PackFile(receiptPath, receiptBytes.LongLength,
            Convert.ToHexString(SHA256.HashData(receiptBytes)))).ToArray();
        foreach (var file in inventory)
        {
            cancel.ThrowIfCancellationRequested();
            string path = SafeFiles.Inside(game, file.Path);
            if (string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)) throw new IOException("The archive cannot replace a pack file.");
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes)
                throw new IOException("Pack file is missing or changed: " + file.Path + ". Verify or rebuild the pack first.");
        }
        long total = inventory.Sum(f => f.Bytes), done = 0;
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Exception? failure = null;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    byte[] buffer = new byte[1024 * 1024];
                    int lastPercent = -1;
                    foreach (var file in inventory)
                    {
                        cancel.ThrowIfCancellationRequested();
                        using var input = new FileStream(SafeFiles.Inside(game, file.Path), FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        using var entry = zip.CreateEntry(file.Path, CompressionLevel.Optimal).Open();
                        long bytes = 0;
                        int count;
                        while ((count = input.Read(buffer)) > 0)
                        {
                            cancel.ThrowIfCancellationRequested();
                            entry.Write(buffer, 0, count); hash.AppendData(buffer, 0, count);
                            bytes += count; done += count;
                            int percent = (int)Math.Min(99, done * 100 / Math.Max(1, total));
                            if (percent != lastPercent)
                            {
                                lastPercent = percent;
                                progress?.Invoke(new(percent, "Packaging and verifying: " + file.Path));
                            }
                        }
                        if (bytes != file.Bytes || Convert.ToHexString(hash.GetHashAndReset()) != file.Sha256)
                            throw new IOException("Pack file is changed: " + file.Path + ". Verify or rebuild the pack first.");
                    }
                }
                output.Flush(true);
            }
            cancel.ThrowIfCancellationRequested();
            SafeFiles.NoLinks(destination);
            // Publish only a complete ZIP. Cancellation/failure keeps an existing export.
            File.Move(temporary, destination, overwrite: true);
        }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            try { if (File.Exists(temporary)) SafeFiles.DeleteOwned(temporary); }
            catch (Exception ex) when (failure is not null && ex is IOException or UnauthorizedAccessException)
            { failure.Data["DiRT2VR.Cleanup"] = ex.ToString(); }
        }
    }

    static byte[] ReadReceipt(string game, string relative)
    {
        string path = SafeFiles.Inside(game, relative);
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Pack receipt is too large.");
        return File.ReadAllBytes(path);
    }
}
