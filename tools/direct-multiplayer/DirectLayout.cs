using System.Security.Cryptography;
using System.Text.Json;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Multiplayer;

internal sealed record PreparedCircuit(CircuitCatalog Catalog, PackFile[] Files);

// The selected database is part of the digest. Model numbers alone can identify
// different circuits on the two PCs.
public sealed record DirectLayout(string Id, string Digest)
{
    public static string SpaId => TrackPacks.Spa.Layouts[0].Id;
    internal static bool Known(string id) => id == SpaId;

    internal static DirectLayout Read(string install, string id, CancellationToken cancel) =>
        VerifyFiles(install, id, cancel, null);

    // The source pack is read-only. Publish the private layout directory only
    // after its bytes match the lobby selection and its race catalog is ready.
    internal PreparedCircuit Prepare(string install, string privateRun, CancellationToken cancel)
    {
        if (!Known(Id))
            throw new IOException("Private circuit preparation currently supports Spa.");
        cancel.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(privateRun);
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        if (root.Equals(source, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use a private run directory outside the installed game.");
        string destination = SafeFiles.Inside(root, "layout");
        if (Path.Exists(destination)) throw new IOException("The private run already contains a layout.");
        string name = "layout-" + Guid.NewGuid().ToString("N") + ".tmp";
        string work = SafeFiles.Inside(root, name);
        string files = Path.Combine(work, "files");
        Directory.CreateDirectory(files);
        try
        {
            var inventory = new List<PackFile>();
            if (VerifyFiles(source, Id, cancel, files, inventory) != this)
                throw new IOException("The custom circuit changed after the lobby checked it. Select it again.");
            string schema = Path.Combine(work, "schema.xml");
            using (var embedded = typeof(DirectLayout).Assembly.GetManifestResourceStream("DirectCircuitSchema")!)
            using (var output = File.Open(schema, FileMode.CreateNew, FileAccess.Write))
                embedded.CopyTo(output);
            string database = SafeFiles.Inside(files, "database/database.bin");
            var catalog = CircuitCatalog.Register(database, schema, TrackPacks.Spa.GetLayout(Id));
            inventory[inventory.FindIndex(f => f.Path == "database/database.bin")] =
                new("database/database.bin", new FileInfo(database).Length, SafeFiles.Hash(database, cancel));
            cancel.ThrowIfCancellationRequested();
            Directory.Move(files, destination);
            return new(catalog, inventory.ToArray());
        }
        finally { SafeFiles.DeleteWorkTree(root, name); }
    }

    static DirectLayout VerifyFiles(string install, string id, CancellationToken cancel, string? output,
                                    List<PackFile>? inventory = null)
    {
        if (!Known(id)) throw new IOException("This layout does not support a Direct circuit race.");
        var pack = TrackPacks.Spa;
        if (!File.Exists(SafeFiles.Inside(install, pack.Receipt)))
            throw new IOException("Install Spa-Francorchamps on this PC using Manage → GRID 2 → Spa-Francorchamps.");
        var receipt = pack.Read(install, verify: false);
        if (!pack.SupportsRace(receipt, id))
            throw new IOException("Install the race-capable version of this layout.");
        var session = receipt.Sessions.Single(s => s.LayoutId == id);
        var files = receipt.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        string folder = $"tracks/usa/{pack.GetLayout(id).Folder}/";
        var selected = files.Values.Where(f => f.Path.StartsWith(folder, StringComparison.Ordinal))
            .Select(f => (Target: f.Path, File: f))
            .Concat(session.Files.Select(f => (Target: f.Path, File: files[f.InstalledPath])))
            .OrderBy(f => f.Target, StringComparer.Ordinal);
        if (output != null)
        {
            var stock = PrivateGame.StockAssets().ToDictionary(a => a.path, StringComparer.OrdinalIgnoreCase);
            foreach (var shared in session.Files)
                if (!stock.TryGetValue(shared.Path, out var asset) ||
                    !asset.sha256.Equals(shared.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Incompatible stock file for this circuit: " + shared.Path);
            if (stock.Keys.Any(p => p.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("The custom circuit folder overlaps a stock track.");
        }
        var assets = new List<(string Target, string Hash)>();
        foreach (var (target, file) in selected)
        {
            cancel.ThrowIfCancellationRequested();
            string path = SafeFiles.Inside(install, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes)
                throw new IOException("Rebuild the selected custom track. A file is missing or changed: " + target);
            if (output != null)
            {
                string copy = SafeFiles.Inside(output, target);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                using (var input = File.OpenRead(path))
                using (var dest = File.Open(copy, FileMode.CreateNew, FileAccess.Write))
                    input.CopyTo(dest);
                path = copy;
            }
            if (SafeFiles.Hash(path, cancel) != file.Sha256)
                throw new IOException("Rebuild the selected custom track. A file is missing or changed: " + target);
            assets.Add((target, file.Sha256));
            inventory?.Add(file with { Path = target });
        }
        var content = new { pack.Id, Layout = id, Version = session.Version ?? receipt.Version,
            Assets = assets.Select(f => new { f.Target, f.Hash }) };
        return new(id, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content))));
    }
}
