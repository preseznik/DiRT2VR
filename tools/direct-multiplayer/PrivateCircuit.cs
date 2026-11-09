using System.Security.Cryptography;
using System.Text.Json;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Multiplayer;

// Called under the same session mutex as PrivateGame.Prepare. Shared game
// files use the existing backup journal. The new track folder moves as a unit.
internal static class PrivateCircuit
{
    sealed record RouteReceipt(string LayoutId, PackFile[] Files);
    static string Route(string id) => "tracks/usa/" + DirectLayout.Pack(id).GetLayout(id).Folder;
    const string Journal = "circuit-route.json";
    const string Retiring = "circuit-route-retiring";

    internal static CircuitCatalog Activate(string cache, string install, DirectLayout selection, CancellationToken cancel)
    {
        SafeFiles.RequireClosed();
        Recover(cache);
        string game = SafeFiles.Inside(cache, "game");
        string relative = Route(selection.Id);
        string route = SafeFiles.Inside(game, relative);
        if (Path.Exists(route) || Path.Exists(SafeFiles.Inside(cache, Retiring)))
            throw new IOException("An existing private circuit folder must be preserved. Check the Direct cache.");
        string build = "circuit-build-" + Guid.NewGuid().ToString("N");
        string work = SafeFiles.Inside(cache, build);
        try
        {
            var prepared = selection.Prepare(install, work, cancel);
            string stage = SafeFiles.Inside(work, "layout");
            var stock = PrivateGame.StockAssets().ToDictionary(a => a.path, StringComparer.OrdinalIgnoreCase);
            var shared = new List<SessionReplacement>();
            var owned = new List<PackFile>();
            foreach (var file in prepared.Files)
            {
                cancel.ThrowIfCancellationRequested();
                if (file.Path.StartsWith(relative + "/", StringComparison.Ordinal))
                {
                    owned.Add(file with { Path = file.Path[(relative.Length + 1)..] });
                    continue;
                }
                var bytes = File.ReadAllBytes(SafeFiles.Inside(stage, file.Path));
                if (Convert.ToHexString(SHA256.HashData(bytes)) != file.Sha256)
                    throw new IOException("The staged circuit changed: " + file.Path);
                shared.Add(new(file.Path, stock[file.Path].sha256.ToUpperInvariant(), bytes));
            }
            SessionFiles.PreparePrivateCircuit(cache, DirectLayout.Pack(selection.Id), shared);
            cancel.ThrowIfCancellationRequested();
            SafeFiles.WriteJson(SafeFiles.Inside(cache, Journal), new RouteReceipt(selection.Id, owned.ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(route)!);
            Directory.Move(SafeFiles.Inside(stage, relative), route);
            CheckRoute(route, owned, true, cancel);
            return prepared.Catalog;
        }
        catch
        {
            Recover(cache);
            throw;
        }
        finally { SafeFiles.DeleteWorkTree(cache, build); }
    }

    internal static void Recover(string cache)
    {
        SessionFiles.RecoverPrivateCircuit(cache);
        string journal = SafeFiles.Inside(cache, Journal);
        if (!File.Exists(journal)) return;
        SafeFiles.RequireClosed();
        var record = SafeFiles.ReadJson<JsonElement>(journal);
        // Earlier builds journalled only Spa's relative file list.
        var receipt = record.ValueKind == JsonValueKind.Array
            ? new RouteReceipt(DirectLayout.SpaId, record.Deserialize<PackFile[]>()!)
            : record.Deserialize<RouteReceipt>();
        if (receipt?.Files is not { Length: > 0 } owned || owned.Any(f => f is null || !SafeFiles.Digest(f.Sha256) || f.Bytes < 0))
            throw new IOException("Invalid private circuit recovery record.");
        foreach (var file in owned) SafeFiles.Relative(file.Path);
        string route = SafeFiles.Inside(SafeFiles.Inside(cache, "game"), Route(receipt.LayoutId));
        string retiring = SafeFiles.Inside(cache, Retiring);
        if (File.Exists(route) || File.Exists(retiring))
            throw new IOException("A file occupies the private circuit recovery folder. Files preserved.");
        if (Directory.Exists(route)) Directory.Move(route, retiring);
        if (Directory.Exists(retiring))
        {
            // Only remove files whose bytes we installed. An unexpected or
            // edited file leaves the folder and its recovery record available.
            var files = CheckRoute(retiring, owned, false, default);
            foreach (var file in files) SafeFiles.DeleteOwned(file);
            RemoveEmptyDirectories(retiring);
        }
        File.Delete(journal);
    }

    static string[] CheckRoute(string root, IReadOnlyList<PackFile> owned, bool requireAll, CancellationToken cancel)
    {
        var expected = owned.ToDictionary(f => SafeFiles.Inside(root, f.Path), StringComparer.OrdinalIgnoreCase);
        var files = SafeFiles.Tree(root).ToArray();
        foreach (var path in files)
            if (!expected.TryGetValue(path, out var entry) || new FileInfo(path).Length != entry.Bytes ||
                SafeFiles.Hash(path, cancel) != entry.Sha256)
                throw new IOException("Private circuit file changed. Files preserved in " + root);
        if (requireAll && files.Length != expected.Count) throw new IOException("The private circuit is incomplete.");
        return files;
    }

    static void RemoveEmptyDirectories(string folder)
    {
        SafeFiles.NoLinks(folder);
        foreach (var child in Directory.EnumerateDirectories(folder)) RemoveEmptyDirectories(child);
        Directory.Delete(folder);
    }
}
