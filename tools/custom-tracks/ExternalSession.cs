namespace DiRT2VR.CustomTracks;

// Recover without the pack/registry: users may remove a dropped-in folder after a crash.
public static class ExternalSession
{
    const string Folder = "DiRT2VR/external-session";
    public sealed record Entry(string Path, string? Original, string Applied, FileAttributes? Attributes);
    public sealed record Journal(int Schema, string Id, Entry[] Entries)
    {
        public bool StagedAdditions { get; init; }
    }
    public static bool Pending(string game) => File.Exists(SafeFiles.Inside(game, Folder + "/pending.json"))
        || File.Exists(SafeFiles.Inside(game, Folder + "/preparing.json"));
    public static void Prepare(string game, ExternalPack pack, string layoutId, Action<int>? afterWrite = null) => PrepareContent(game, pack, layoutId, null, afterWrite);
    public static void PrepareContent(string game, ExternalPack? pack, string? layoutId, ExternalPack? car, Action<int>? afterWrite = null, bool race = false, string raceStart = "auto")
    {
        SafeFiles.RequireClosed();
        if (Pending(game)) throw new IOException("External content recovery is pending.");
        var writes = new List<(Entry Entry, string Source)>();
        if (pack is not null) writes.AddRange(TrackWrites(game, pack, layoutId!, race, raceStart, default));
        if (car is not null)
        {
            car.Verify(); car.RequireDependencies(game);
            string code = car.Manifest!.Car!.Code;
            foreach (string prefix in new[] { "cars/", "interiors/" })
                RequireEmptyAssetFolder(game, prefix + code);
            foreach (var file in car.Manifest.Files)
            {
                string target = file.Path["payload/".Length..];
                writes.Add((new(target, null, file.Sha256, null), SafeFiles.Inside(car.Root, file.Path)));
            }
        }
        Apply(game, SafeFiles.Inside(game, Folder), writes, ValidateExternal, afterWrite, default);
    }

    internal static void RequireEmptyAssetFolder(string game, string relative)
    {
        string target = SafeFiles.Inside(game, relative);
        if (File.Exists(target) || Directory.Exists(target) && SafeFiles.Tree(target).Any())
            throw new IOException("Content destination contains existing files: " + target);
    }

    internal static IEnumerable<(Entry Entry, string Source)> TrackWrites(string game, ExternalPack pack,
        string layoutId, bool race, string raceStart, CancellationToken cancel)
    {
        pack.Verify(cancel);
        pack.RequireDependencies(game, cancel);
        var layout = pack.Layout(layoutId);
        if (race && (!pack.Manifest!.Modes.Contains("desktop-race") || layout.MaximumOpponents < 1))
            throw new IOException("This external layout does not support racing.");
        if (raceStart is not ("auto" or "grid" or "staggered") ||
            (race && raceStart == "staggered" && layout.RaceStart != "staggered") ||
            (race && raceStart == "grid" && layout.RaceStart == "staggered" && layout.GridMaximumOpponents == 0))
            throw new IOException("This external layout does not support the selected race start.");
        var raceSession = raceStart == "grid" && layout.RaceStart == "staggered" ? layout.GridRaceSession : layout.RaceSession;
        RequireEmptyAssetFolder(game, "tracks/usa/" + layout.Folder);
        foreach (var file in pack.Manifest!.Files.Where(f => f.Path.StartsWith("payload/tracks/usa/" + layout.Folder + "/", StringComparison.Ordinal)))
        {
            cancel.ThrowIfCancellationRequested();
            string target = file.Path["payload/".Length..];
            if (File.Exists(SafeFiles.Inside(game, target))) throw new IOException("External track destination already exists; it was preserved: " + target);
            var replacement = race ? raceSession?.SingleOrDefault(s => s.Target == target) : null;
            var applied = replacement is null ? file : pack.Manifest.Files.Single(f => f.Path == replacement.File);
            yield return (new(target, null, applied.Sha256, null), SafeFiles.Inside(pack.Root, applied.Path));
        }
        foreach (var item in layout.Session)
        {
            var file = pack.Manifest.Files.Single(f => f.Path == item.File);
            var dependency = pack.Manifest.Dependencies.Single(d => d.Path == item.Target);
            string target = SafeFiles.Inside(game, item.Target);
            yield return (new(item.Target, dependency.Sha256, file.Sha256, File.GetAttributes(target)), SafeFiles.Inside(pack.Root, file.Path));
        }
    }

    internal static void Apply(string game, string folder, IReadOnlyList<(Entry Entry, string Source)> writes,
        Action<Journal> validate, Action<int>? afterWrite, CancellationToken cancel)
    {
        string id = Guid.NewGuid().ToString("N");
        var journal = new Journal(1, id, writes.Select(w => w.Entry).ToArray()) { StagedAdditions = true };
        validate(journal);
        string pending = SafeFiles.Inside(folder, "pending.json");
        string preparing = SafeFiles.Inside(folder, "preparing.json");
        if (File.Exists(pending) || File.Exists(preparing)) throw new IOException("Content recovery is pending.");
        string work = SafeFiles.Inside(folder, id);
        if (Path.Exists(work)) throw new IOException("Content staging folder already exists.");
        SafeFiles.WriteJson(preparing, journal);
        try
        {
            Directory.CreateDirectory(work);
            for (int i = 0; i < writes.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var (entry, source) = writes[i];
                string target = SafeFiles.Inside(game, entry.Path);
                if (entry.Original is null && Path.Exists(target))
                    throw new IOException("Content destination already exists: " + entry.Path);
                string copy = SafeFiles.Inside(work, i + (entry.Original is null ? ".new" : ".original"));
                File.Copy(entry.Original is null ? source : target, copy);
                if (SafeFiles.Hash(copy, cancel) != (entry.Original ?? entry.Applied))
                    throw new IOException("Content changed during preparation: " + entry.Path);
            }
            File.Move(preparing, pending);
        }
        catch
        {
            if (File.Exists(preparing)) DiscardPreparation(folder, preparing, journal);
            throw;
        }
        for (int i = 0; i < writes.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var (entry, source) = writes[i]; string target = SafeFiles.Inside(game, entry.Path);
            if (entry.Original is null)
            {
                string temporary = SafeFiles.Inside(folder, $"{id}/{i}.new");
                if (SafeFiles.Hash(temporary, cancel) != entry.Applied) throw new IOException("Staged content changed: " + entry.Path);
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
    static void ValidateExternal(Journal j) => Validate(j, 4096, path => TrackPacks.SessionTargets.Contains(path),
        path => ExternalPacks.AssetPath(path) || ExternalPacks.CarAssetPath(path));

    internal static void Validate(Journal j, int maxEntries, Func<string,bool> sharedTarget, Func<string,bool> newTarget)
    {
        if (j.Schema != 1 || !Guid.TryParseExact(j.Id, "N", out _) || j.Entries is null || j.Entries.Length < 1 || j.Entries.Length > maxEntries ||
            j.Entries.Any(e => e is null) || j.Entries.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != j.Entries.Length)
            throw new IOException("Invalid external recovery journal; files kept.");
        foreach (var e in j.Entries)
        {
            SafeFiles.Relative(e.Path);
            bool shared = sharedTarget(e.Path);
            if ((!shared && !newTarget(e.Path)) || !SafeFiles.Digest(e.Applied) ||
                (shared ? !SafeFiles.Digest(e.Original!) || e.Attributes is null || (e.Attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 : e.Original is not null || e.Attributes is not null)) throw new IOException("Unsafe external recovery target; files kept.");
        }
    }
    public static void Recover(string game)
    {
        SafeFiles.RequireClosed(); if (!Pending(game)) return;
        Recover(game, SafeFiles.Inside(game, Folder), ValidateExternal);
    }

    internal static void Recover(string game, string folder, Action<Journal> validate)
    {
        string path = SafeFiles.Inside(folder, "pending.json");
        string preparing = SafeFiles.Inside(folder, "preparing.json");
        if (File.Exists(preparing))
        {
            SafeFiles.RequireClosed();
            if (File.Exists(path)) throw new IOException("Two content recovery records are present; files preserved.");
            var staged = SafeFiles.ReadJson<Journal>(preparing);
            validate(staged);
            DiscardPreparation(folder, preparing, staged);
            return;
        }
        if (!File.Exists(path)) return;
        SafeFiles.RequireClosed();
        var journal = SafeFiles.ReadJson<Journal>(path);
        validate(journal);
        for (int i = 0; i < journal.Entries.Length; i++)
        {
            var e = journal.Entries[i];
            if (e.Original is not null && SafeFiles.Hash(SafeFiles.Inside(folder, $"{journal.Id}/{i}.original")) != e.Original)
                throw new IOException("External recovery backup changed; files kept.");
        }
        var conflicts = new List<string>();
        for (int i = journal.Entries.Length - 1; i >= 0; i--)
        {
            var e = journal.Entries[i]; string target = SafeFiles.Inside(game, e.Path);
            // A remaining staged addition means the atomic move never acquired the game path.
            string staged = SafeFiles.Inside(folder, $"{journal.Id}/{i}.new");
            if (journal.StagedAdditions && e.Original is null && File.Exists(staged)) continue;
            string? current = File.Exists(target) ? SafeFiles.Hash(target) : null;
            if (current != e.Applied && current != e.Original && current is not null) { conflicts.Add(e.Path); continue; }
            if (e.Original is null)
            {
                if (current == e.Applied)
                {
                    if (journal.StagedAdditions) File.Move(target, staged);
                    else SafeFiles.DeleteOwned(target);
                }
            }
            else
            {
                if (current != e.Original) SafeFiles.Atomic(target, File.ReadAllBytes(SafeFiles.Inside(folder, $"{journal.Id}/{i}.original")));
                File.SetAttributes(target, e.Attributes!.Value);
                if (SafeFiles.Hash(target) != e.Original) throw new IOException("External recovery verification failed.");
            }
        }
        if (conflicts.Count > 0) throw new IOException("External edits preserved; recovery is still pending: " + string.Join(", ", conflicts));
        string recovered = SafeFiles.Inside(folder, $"{journal.Id}/recovered.json");
        Directory.CreateDirectory(Path.GetDirectoryName(recovered)!); File.Move(path, recovered);
        for (int i = 0; i < journal.Entries.Length; i++)
        {
            var e = journal.Entries[i];
            string copy = SafeFiles.Inside(folder, $"{journal.Id}/{i}." + (e.Original is null ? "new" : "original"));
            try { if (File.Exists(copy) && SafeFiles.Hash(copy) == (e.Original ?? e.Applied)) SafeFiles.DeleteOwned(copy); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Until the preparing record is renamed, the game tree has not been written.
    // Only the numbered scratch files belong to this interrupted copy.
    static void DiscardPreparation(string folder, string marker, Journal journal)
    {
        string work = SafeFiles.Inside(folder, journal.Id);
        if (Directory.Exists(work))
        {
            var expected = journal.Entries.Select((entry, i) => SafeFiles.Inside(work, i + (entry.Original == null ? ".new" : ".original")))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var present = Directory.GetFileSystemEntries(work);
            foreach (var path in present)
            {
                SafeFiles.NoLinks(path);
                if (!expected.Contains(path) || Directory.Exists(path)) throw new IOException("Unexpected file in content staging; files preserved.");
            }
            foreach (var path in present) SafeFiles.DeleteOwned(path);
            Directory.Delete(work);
        }
        File.Delete(marker);
    }
}
