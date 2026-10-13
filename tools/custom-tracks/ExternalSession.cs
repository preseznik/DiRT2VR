namespace DiRT2VR.CustomTracks;

// Recover without the pack/registry: users may remove a dropped-in folder after a crash.
public static class ExternalSession
{
    const string Folder = "DiRT2VR/external-session";
    public sealed record Entry(string Path, string? Original, string Applied, FileAttributes? Attributes);
    public sealed record Journal(int Schema, string Id, Entry[] Entries);
    public static bool Pending(string game) => File.Exists(SafeFiles.Inside(game, Folder + "/pending.json"));
    public static void Prepare(string game, ExternalPack pack, string layoutId, Action<int>? afterWrite = null)
    {
        SafeFiles.RequireClosed();
        if (Pending(game)) throw new IOException("External content recovery is pending.");
        pack.Verify(); pack.RequireDependencies(game);
        var layout = pack.Layout(layoutId);
        string trackRoot = SafeFiles.Inside(game, "tracks/usa/" + layout.Folder);
        if (Directory.Exists(trackRoot) && SafeFiles.Tree(trackRoot).Any()) throw new IOException("External track destination contains existing files; they were preserved: " + trackRoot);
        var writes = new List<(Entry Entry, string Source)>();
        foreach (var file in pack.Manifest!.Files.Where(f => f.Path.StartsWith("payload/tracks/usa/" + layout.Folder + "/", StringComparison.Ordinal)))
        {
            string target = file.Path["payload/".Length..];
            if (File.Exists(SafeFiles.Inside(game, target))) throw new IOException("External track destination already exists; it was preserved: " + target);
            writes.Add((new(target, null, file.Sha256, null), SafeFiles.Inside(pack.Root, file.Path)));
        }
        foreach (var item in layout.Session)
        {
            var file = pack.Manifest.Files.Single(f => f.Path == item.File);
            var dependency = pack.Manifest.Dependencies.Single(d => d.Path == item.Target);
            string target = SafeFiles.Inside(game, item.Target);
            writes.Add((new(item.Target, dependency.Sha256, file.Sha256, File.GetAttributes(target)), SafeFiles.Inside(pack.Root, file.Path)));
        }
        string id = Guid.NewGuid().ToString("N");
        var journal = new Journal(1, id, writes.Select(w => w.Entry).ToArray()); Validate(journal);
        for (int i = 0; i < writes.Count; i++)
        {
            var entry = writes[i].Entry;
            if (entry.Original is null) continue;
            string original = SafeFiles.Inside(game, entry.Path), backup = SafeFiles.Inside(game, $"{Folder}/{id}/{i}.original");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(original, backup);
            if (SafeFiles.Hash(backup) != entry.Original) throw new IOException("External content backup verification failed.");
        }
        SafeFiles.WriteJson(SafeFiles.Inside(game, Folder + "/pending.json"), journal);
        for (int i = 0; i < writes.Count; i++)
        {
            var (entry, source) = writes[i]; string target = SafeFiles.Inside(game, entry.Path);
            if (entry.Original is null)
            {
                string temporary = SafeFiles.Inside(game, $"{Folder}/{id}/{i}.new");
                Directory.CreateDirectory(Path.GetDirectoryName(temporary)!); File.Copy(source, temporary, false);
                if (SafeFiles.Hash(temporary) != entry.Applied) throw new IOException("External payload changed while preparing.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(temporary, target, false);
            }
            else
            {
                if (SafeFiles.Hash(target) != entry.Original) throw new IOException("Game file changed during external preparation; recovery was retained.");
                byte[] bytes = File.ReadAllBytes(source);
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != entry.Applied) throw new IOException("External session data changed while preparing.");
                SafeFiles.Atomic(target, bytes);
            }
            afterWrite?.Invoke(i);
        }
    }
    static void Validate(Journal j)
    {
        if (j.Schema != 1 || !Guid.TryParseExact(j.Id, "N", out _) || j.Entries is null || j.Entries.Length is < 1 or > 4096 ||
            j.Entries.Any(e => e is null) || j.Entries.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != j.Entries.Length)
            throw new IOException("Invalid external recovery journal; files kept.");
        foreach (var e in j.Entries)
        {
            SafeFiles.Relative(e.Path);
            bool shared = TrackPacks.SessionTargets.Contains(e.Path);
            if ((!shared && !ExternalPacks.AssetPath(e.Path)) || !SafeFiles.Digest(e.Applied) ||
                (shared ? !SafeFiles.Digest(e.Original!) || e.Attributes is null || (e.Attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 : e.Original is not null || e.Attributes is not null)) throw new IOException("Unsafe external recovery target; files kept.");
        }
    }
    public static void Recover(string game)
    {
        SafeFiles.RequireClosed(); if (!Pending(game)) return;
        string path = SafeFiles.Inside(game, Folder + "/pending.json");
        var journal = SafeFiles.ReadJson<Journal>(path); Validate(journal);
        for (int i = 0; i < journal.Entries.Length; i++)
        {
            var e = journal.Entries[i];
            if (e.Original is not null && SafeFiles.Hash(SafeFiles.Inside(game, $"{Folder}/{journal.Id}/{i}.original")) != e.Original)
                throw new IOException("External recovery backup changed; files kept.");
        }
        var conflicts = new List<string>();
        for (int i = journal.Entries.Length - 1; i >= 0; i--)
        {
            var e = journal.Entries[i]; string target = SafeFiles.Inside(game, e.Path);
            string? current = File.Exists(target) ? SafeFiles.Hash(target) : null;
            if (current != e.Applied && current != e.Original && current is not null) { conflicts.Add(e.Path); continue; }
            if (e.Original is null)
            {
                if (current == e.Applied) SafeFiles.DeleteOwned(target);
            }
            else
            {
                if (current != e.Original) SafeFiles.Atomic(target, File.ReadAllBytes(SafeFiles.Inside(game, $"{Folder}/{journal.Id}/{i}.original")));
                File.SetAttributes(target, e.Attributes!.Value);
                if (SafeFiles.Hash(target) != e.Original) throw new IOException("External recovery verification failed.");
            }
        }
        if (conflicts.Count > 0) throw new IOException("External edits preserved; recovery is still pending: " + string.Join(", ", conflicts));
        string recovered = SafeFiles.Inside(game, $"{Folder}/{journal.Id}/recovered.json");
        Directory.CreateDirectory(Path.GetDirectoryName(recovered)!); File.Move(path, recovered);
        for (int i = 0; i < journal.Entries.Length; i++)
        {
            string backup = SafeFiles.Inside(game, $"{Folder}/{journal.Id}/{i}.original");
            try { if (File.Exists(backup) && SafeFiles.Hash(backup) == journal.Entries[i].Original) SafeFiles.DeleteOwned(backup); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
