using DiRT2VR.CustomTracks;
using System.Text.Json;

namespace DiRT2VR.CustomCars;

public static class CarInstallation
{
    const string Folder = "DiRT2VR/custom-car-install";
    public const string Journal = Folder + "/pending.json";
    sealed record Entry(string Path, string? OriginalSha256, string? AppliedSha256, bool ReadOnly);
    sealed record Pending(int Schema, string Id, Entry[] Files, bool Committed);
    static string Current(string path) => File.Exists(path) ? SafeFiles.Hash(path) : "";
    public static void Install(string game, string staging)
    {
        SafeFiles.RequireClosed(); SessionFiles.Recover(game); Recover(game);
        var after = CarPack.Read(staging);
        Apply(game, staging, after);
    }
    public static void Uninstall(string game)
    {
        SafeFiles.RequireClosed(); SessionFiles.Recover(game); Recover(game);
        if (!File.Exists(SafeFiles.Inside(game, CarPack.Receipt))) return;
        Apply(game, null, null);
    }
    static void Apply(string game, string? staging, CarReceipt? after)
    {
        string receiptPath = SafeFiles.Inside(game, CarPack.Receipt);
        CarReceipt? before = File.Exists(receiptPath) ? CarPack.Read(game, false) : null;
        var owned = before?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        // Missing owned files can be repaired; modified files and unknown assets are preserved.
        foreach (var file in owned.Values)
        {
            string path = SafeFiles.Inside(game, file.Path);
            if (File.Exists(path) && SafeFiles.Hash(path) != file.Sha256) throw new IOException("Custom-car file changed outside DiRT2VR; preserved: " + file.Path);
        }
        var next = after?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        var paths = owned.Keys.Concat(next.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).Append(CarPack.Receipt).ToArray();
        string id = Guid.NewGuid().ToString("N"), work = SafeFiles.Inside(game, Folder + "/" + id);
        var entries = new List<Entry>();
        var replacements = new List<byte[]?>();
        foreach (string relative in paths)
        {
            string target = SafeFiles.Inside(game, relative); bool exists = File.Exists(target);
            if (exists && relative != CarPack.Receipt && !owned.ContainsKey(relative)) throw new IOException("Unowned f04 asset already exists; preserved: " + relative);
            byte[]? bytes = relative == CarPack.Receipt ? after is null ? null : JsonSerializer.SerializeToUtf8Bytes(after, SafeFiles.Json) : next.ContainsKey(relative) ? File.ReadAllBytes(SafeFiles.Inside(staging!, relative)) : null;
            string? hash = bytes is null ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            if (relative != CarPack.Receipt && hash is not null && hash != next[relative].Sha256) throw new IOException("Staged car file changed during installation.");
            string? original = exists ? SafeFiles.Hash(target) : null;
            if (exists)
            {
                string backup = SafeFiles.Inside(work, entries.Count + ".original"); Directory.CreateDirectory(work); File.Copy(target, backup);
                if (SafeFiles.Hash(backup) != original) throw new IOException("Custom-car backup changed during preparation.");
            }
            entries.Add(new(relative, original, hash, exists && (File.GetAttributes(target) & FileAttributes.ReadOnly) != 0)); replacements.Add(bytes);
        }
        SafeFiles.RequireClosed();
        var pending = new Pending(1, id, entries.ToArray(), false); SafeFiles.WriteJson(SafeFiles.Inside(game, Journal), pending);
        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; string target = SafeFiles.Inside(game, entry.Path);
                if (Current(target) != (entry.OriginalSha256 ?? "")) throw new IOException("Custom-car target changed during installation; backup retained.");
                if (replacements[i] is { } bytes) SafeFiles.Atomic(target, bytes); else if (File.Exists(target)) SafeFiles.DeleteOwned(target);
            }
            if (after is not null) CarPack.Verify(game, after);
            SafeFiles.WriteJson(SafeFiles.Inside(game, Journal), pending with { Committed = true });
            Recover(game);
        }
        catch { Recover(game); throw; }
    }
    public static void Recover(string game)
    {
        SafeFiles.RequireClosed(); string journal = SafeFiles.Inside(game, Journal);
        if (!File.Exists(journal)) return;
        var pending = SafeFiles.ReadJson<Pending>(journal);
        if (pending.Schema != 1 || !Guid.TryParseExact(pending.Id, "N", out _) || pending.Files is null || pending.Files.Length is < 1 or > 513 ||
            pending.Files.Any(e => e is null || !(CarPack.Owns(e.Path) || e.Path == CarPack.Receipt) || (e.OriginalSha256 is not null && !SafeFiles.Digest(e.OriginalSha256)) || (e.AppliedSha256 is not null && !SafeFiles.Digest(e.AppliedSha256))) ||
            pending.Files.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pending.Files.Length) throw new IOException("Invalid custom-car installation journal; files preserved.");
        string work = SafeFiles.Inside(game, Folder + "/" + pending.Id);
        // Validate every required backup and target before any recovery mutation.
        for (int i = 0; i < pending.Files.Length; i++)
        {
            var entry = pending.Files[i]; string current = Current(SafeFiles.Inside(game, entry.Path));
            if (!pending.Committed && entry.OriginalSha256 is not null && Current(SafeFiles.Inside(work, i + ".original")) != entry.OriginalSha256) throw new IOException("Custom-car backup missing or changed; files preserved.");
            if (pending.Committed ? current != (entry.AppliedSha256 ?? "") : current != (entry.OriginalSha256 ?? "") && current != (entry.AppliedSha256 ?? "")) throw new IOException("External custom-car edit preserved; recovery remains pending: " + entry.Path);
        }
        if (!pending.Committed)
            for (int i = pending.Files.Length - 1; i >= 0; i--)
            {
                var entry = pending.Files[i]; string target = SafeFiles.Inside(game, entry.Path);
                if (entry.OriginalSha256 is not null) { SafeFiles.Atomic(target, File.ReadAllBytes(SafeFiles.Inside(work, i + ".original"))); SafeFiles.SetReadOnly(target, entry.ReadOnly); }
                else if (File.Exists(target)) SafeFiles.DeleteOwned(target);
            }
        Directory.CreateDirectory(work); File.Move(journal, SafeFiles.Inside(work, "recovered.json"));
        for (int i = 0; i < pending.Files.Length; i++)
        {
            string backup = SafeFiles.Inside(work, i + ".original");
            if (File.Exists(backup) && SafeFiles.Hash(backup) == pending.Files[i].OriginalSha256) SafeFiles.DeleteOwned(backup);
        }
    }
}
