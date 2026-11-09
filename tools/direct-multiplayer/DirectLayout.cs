using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Multiplayer;

internal sealed record PreparedCircuit(CircuitCatalog Catalog, PackFile[] Files);

// The selected database is part of the digest. Model numbers alone can identify
// different circuits on the two PCs.
public sealed record DirectLayout(string Id, string Digest, bool? ExternalCircuit = null)
{
    public static string SpaId => TrackPacks.Spa.Layouts[0].Id;
    internal static bool Known(string id) => TrackPacks.DirectRacePacks.Any(pack => pack.IsLayout(id));
    internal static TrackPack Pack(string id) => TrackPacks.ForLayout(id);

    internal static DirectLayout Read(string install, string id, CancellationToken cancel)
    {
        var pack = Pack(id);
        if (pack.External is not { } external) return VerifyRaceAssets(install, pack, id, cancel, null);
        external.Verify(cancel);
        external.RequireDependencies(install, cancel);
        return new(id, external.ManifestHash, external.Layout(id).Circuit);
    }

    // The source pack is read-only. Publish the private layout directory only
    // after its bytes match the lobby selection and its race catalog is ready.
    internal PreparedCircuit Prepare(string install, string privateRun, CancellationToken cancel)
    {
        var pack = Pack(Id);
        if (pack.External != null) throw new IOException("The Direct race worker cannot start external tracks yet.");
        var layout = pack.GetLayout(Id);
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
            if (VerifyRaceAssets(source, pack, Id, cancel, files, inventory) != this)
                throw new IOException("The custom circuit changed after the lobby checked it. Select it again.");
            string schema = CircuitCatalog.WriteSchema(work);
            string database = SafeFiles.Inside(files, "database/database.bin");
            var catalog = CircuitCatalog.Register(database, schema, layout);
            RecordChange("database/database.bin");
            if (pack == TrackPacks.Nordschleife || !layout.Circuit)
            {
                string relative = $"tracks/usa/{layout.Folder}/route_0/progress_track.xml";
                string progress = SafeFiles.Inside(files, relative);
                byte[] original = File.ReadAllBytes(progress);
                byte[] corrected = layout.Circuit ? Nordschleife.CircuitProgress.Patch(original) : StageProgress(original);
                SafeFiles.Atomic(progress, corrected);
                RecordChange(relative);
            }
            cancel.ThrowIfCancellationRequested();
            Directory.Move(files, destination);
            return new(catalog, inventory.ToArray());

            void RecordChange(string relative)
            {
                string path = SafeFiles.Inside(files, relative);
                int index = inventory.FindIndex(file => file.Path == relative);
                if (index < 0) throw new IOException("Missing circuit file in the pack receipt: " + relative);
                inventory[index] = new(relative, new FileInfo(path).Length, SafeFiles.Hash(path, cancel));
            }
        }
        finally { SafeFiles.DeleteWorkTree(root, name); }
    }

    // Normal Race uses its opening finish gate to initialize demo lap timing.
    // Stock network Raid stages use distinct start, time and finish gates.
    static byte[] StageProgress(byte[] original)
    {
        using var input = new MemoryStream(original);
        var file = new XmlFile(input);
        var data = file.Document;
        var route = data.SelectSingleNode("/progress_track_data/routes[@num_routes='1']/route[@id='0']") as XmlElement;
        var splits = route?.SelectNodes("split")?.Cast<XmlElement>().ToArray();
        int gateCount = data.SelectNodes("/progress_track_data/gates/gate")!.Count;
        if (data.SelectSingleNode("/progress_track_data/track/@type")?.Value != "point_to_point" ||
            data.SelectNodes("/progress_track_data/routes/route")!.Count != 1 || splits is not { Length: >= 2 } ||
            !int.TryParse(route!.GetAttribute("num_splits"), out int count) || count != splits.Length ||
            splits[0].GetAttribute("gate") != "1" || splits[0].GetAttribute("type") is not ("start" or "finish") ||
            splits[^1].GetAttribute("type") != "finish" || splits.Skip(1).SkipLast(1).Any(s => s.GetAttribute("type") != "time"))
            throw new IOException("Unsupported point-to-point timing route.");
        int previous = -1;
        foreach (var split in splits)
        {
            if (!int.TryParse(split.GetAttribute("gate"), out int gate) || gate <= previous || gate >= gateCount)
                throw new IOException("Unsupported point-to-point timing gate.");
            previous = gate;
        }
        if (splits[0].GetAttribute("type") == "start") return original;
        splits[0].SetAttribute("type", "start");
        using var output = new MemoryStream();
        file.Write(output, XmlType.BinXml);
        return output.ToArray();
    }

    // Asset preparation is independent of the event types the Direct worker can run.
    internal static DirectLayout VerifyRaceAssets(string install, TrackPack pack, string id, CancellationToken cancel, string? output,
                                    List<PackFile>? inventory = null)
    {
        if (!File.Exists(SafeFiles.Inside(install, pack.Receipt)))
            throw new IOException($"Install {pack.Name} on this PC using Manage.");
        var receipt = pack.Read(install, verify: false);
        if (!pack.SupportsRace(receipt, id))
            throw new IOException("Install the race-capable version of this layout.");
        var session = receipt.Sessions.Single(s => s.LayoutId == id);
        var files = receipt.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        string folder = $"tracks/usa/{pack.GetLayout(id).Folder}/";
        var selected = files.Values.Where(f => f.Path.StartsWith(folder, StringComparison.Ordinal))
            .ToDictionary(f => f.Path, StringComparer.Ordinal);
        if (pack == TrackPacks.Pikes)
            foreach (var (target, source) in SessionFiles.PikesRacePaths(id, "grid"))
                selected[target] = files[source];
        foreach (var shared in session.Files)
            selected.Add(shared.Path, files[shared.InstalledPath]);
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
        foreach (var (target, file) in selected.OrderBy(f => f.Key, StringComparer.Ordinal))
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
