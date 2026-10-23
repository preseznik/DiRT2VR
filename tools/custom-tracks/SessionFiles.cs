namespace DiRT2VR.CustomTracks;

public sealed record SessionReplacement(string Path, string OriginalSha256, byte[] Bytes);

// One journal covers shared custom-track files and the Nordschleife session route.
// The fixed allowlist is compiled into the launcher, not supplied by a download.
public static class SessionFiles
{
    const string Folder = "DiRT2VR/custom-track-session";
    sealed record Entry(string Path, string OriginalSha256, string AppliedSha256);
    sealed record Pending(int Schema, string Id, Entry[] Files);
    static readonly string[] CarTargets = ["database/database.bin", "cars/class_mappings.xml", "audio/carsetup.bin", "audio/carsetup_ai.bin", "audio/dictionaries.bin"];
    public static void Prepare(string game, string? layoutId, Func<byte[], byte[]>? nordschleifeProgress = null,
        IReadOnlyList<SessionReplacement>? carFiles = null, Func<byte[], byte[]>? carDatabase = null, string? pikesRaceStart = null)
    {
        SafeFiles.RequireClosed(); Recover(game);
        var entries = new List<Entry>(); var replacements = new List<byte[]>();
        if (layoutId is not null)
        {
            var pack = TrackPacks.ForLayout(layoutId);
            if (pack.External is not null) throw new IOException("External packs require the external session transaction.");
            var receipt = pack.Read(game);
            var session = receipt.Sessions.SingleOrDefault(s => s.LayoutId == pack.GetLayout(layoutId).Id)
                ?? throw new IOException("This layout is not installed. Use Manage > Rebuild from source to add it.");
            entries.AddRange(session.Files.Select(f => new Entry(f.Path, f.OriginalSha256,
                receipt.Files.Single(p => p.Path == f.InstalledPath).Sha256)));
            replacements.AddRange(session.Files.Select(f => File.ReadAllBytes(SafeFiles.Inside(game, f.InstalledPath))));
            if (pikesRaceStart is not null)
            {
                if (pack != TrackPacks.Pikes || pikesRaceStart is not ("grid" or "staggered")) throw new IOException("Invalid Pikes Peak race start.");
                string variant = pikesRaceStart == "grid" ? "grid-race" : "race";
                foreach (string name in PikesRaceFiles)
                {
                    string path = $"tracks/usa/{pack.GetLayout(layoutId).Folder}/route_0/{name}";
                    string owned = $"{pack.Support}/{layoutId}/{variant}/{name}";
                    var original = receipt.Files.Single(f => f.Path == path);
                    var race = receipt.Files.Single(f => f.Path == owned);
                    byte[] bytes = File.ReadAllBytes(SafeFiles.Inside(game, owned));
                    if (Digest(bytes) != race.Sha256) throw new IOException("Pikes Peak race file changed during preparation.");
                    entries.Add(new(path, original.Sha256, race.Sha256)); replacements.Add(bytes);
                }
            }
            if (nordschleifeProgress is not null)
            {
                if (pack != TrackPacks.Nordschleife) throw new IOException("The circuit progress correction is only for Nordschleife.");
                string path = ProgressTarget(pack.GetLayout(layoutId));
                var original = File.ReadAllBytes(SafeFiles.Inside(game, path));
                string hash = receipt.Files.Single(f => f.Path == path).Sha256;
                if (Digest(original) != hash) throw new IOException("Nordschleife progress changed during preparation; files preserved.");
                var corrected = nordschleifeProgress(original);
                entries.Add(new(path, hash, Digest(corrected)));
                replacements.Add(corrected);
            }
        }
        else if (nordschleifeProgress is not null || pikesRaceStart is not null) throw new IOException("A route correction requires its installed track.");
        if (carFiles is not null)
        {
            if (carFiles.Count != CarTargets.Length || carFiles.Any(f => f is null) || carFiles.Select(f => f.Path).Distinct().Count() != CarTargets.Length ||
                carFiles.Any(f => !CarTargets.Contains(f.Path) || !SafeFiles.Digest(f.OriginalSha256) || f.Bytes is null) || carDatabase is null)
                throw new IOException("Invalid custom-car session replacements.");
            foreach (var file in carFiles)
            {
                int existing = entries.FindIndex(e => e.Path == file.Path);
                if (existing >= 0)
                {
                    if (file.Path != "database/database.bin" || entries[existing].OriginalSha256 != file.OriginalSha256)
                        throw new IOException("Track and car source versions differ; files preserved.");
                    // Retain the track database as input to the car transformation below.
                    continue;
                }
                entries.Add(new(file.Path, file.OriginalSha256, Digest(file.Bytes))); replacements.Add(file.Bytes);
            }
            int database = entries.FindIndex(e => e.Path == "database/database.bin");
            replacements[database] = carDatabase(replacements[database]);
            entries[database] = entries[database] with { AppliedSha256 = Digest(replacements[database]) };
        }
        else if (carDatabase is not null) throw new IOException("A car database patch requires its verified session files.");
        if (entries.Count == 0 || entries.Select(e => e.Path).Distinct().Count() != entries.Count) throw new IOException("Invalid custom-content session.");
        for (int i = 0; i < entries.Count; i++)
            if (Digest(replacements[i]) != entries[i].AppliedSha256) throw new IOException("Custom session file changed during preparation.");
        var id = Guid.NewGuid().ToString("N");
        var folder = SafeFiles.Inside(game, Folder);
        Directory.CreateDirectory(folder);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; var target = SafeFiles.Inside(game, entry.Path);
            if (SafeFiles.Hash(target) != entry.OriginalSha256) throw new IOException("This track needs the original game file: " + entry.Path + ". External edits were preserved.");
            var backup = SafeFiles.Inside(folder, id + "/" + i + ".original");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(target, backup);
            if (SafeFiles.Hash(backup) != entry.OriginalSha256) throw new IOException("Session backup verification failed.");
        }
        SafeFiles.WriteJson(SafeFiles.Inside(folder, "pending.json"), new Pending(1, id, entries.ToArray()));
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; var target = SafeFiles.Inside(game, entry.Path);
            if (SafeFiles.Hash(target) != entry.OriginalSha256) throw new IOException("Game file changed during session preparation; backup retained.");
            var bytes = replacements[i];
            SafeFiles.Atomic(target, bytes);
        }
    }
    static string Digest(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    static string ProgressTarget(Layout layout) => $"tracks/usa/{layout.Folder}/route_0/progress_track.xml";
    public static readonly string[] PikesRaceFiles = ["grids.pssg", "progress_track.xml", "ai_track.xml", "dev_ai_track.xml"];
    static bool AllowedTarget(string path) => TrackPacks.SessionTargets.Contains(path) || CarTargets.Contains(path) ||
        TrackPacks.Nordschleife.Layouts.Any(l => path == ProgressTarget(l)) ||
        TrackPacks.Pikes.Layouts.Any(l => PikesRaceFiles.Any(n => path == $"tracks/usa/{l.Folder}/route_0/{n}"));
    public static void Recover(string game)
    {
        SafeFiles.RequireClosed();
        var folder = SafeFiles.Inside(game, Folder);
        var journal = SafeFiles.Inside(folder, "pending.json");
        if (!File.Exists(journal)) return;
        var pending = SafeFiles.ReadJson<Pending>(journal);
        if (pending.Schema != 1 || !Guid.TryParseExact(pending.Id, "N", out _) || pending.Files is null || pending.Files.Length == 0 || pending.Files.Any(f => f is null) ||
            pending.Files.Select(f => f.Path).Distinct().Count() != pending.Files.Length ||
            pending.Files.Any(f => !AllowedTarget(f.Path) || !SafeFiles.Digest(f.OriginalSha256) || !SafeFiles.Digest(f.AppliedSha256)))
            throw new IOException("Invalid custom-track session journal; files preserved.");
        for (int i = 0; i < pending.Files.Length; i++)
            if (SafeFiles.Hash(SafeFiles.Inside(folder, pending.Id + "/" + i + ".original")) != pending.Files[i].OriginalSha256)
                throw new IOException("Custom session backup changed; files preserved.");
        var conflicts = new List<string>();
        for (int i = 0; i < pending.Files.Length; i++)
        {
            var entry = pending.Files[i]; var target = SafeFiles.Inside(game, entry.Path);
            var current = File.Exists(target) ? SafeFiles.Hash(target) : "";
            if (current == entry.AppliedSha256 || current == "")
                SafeFiles.Atomic(target, File.ReadAllBytes(SafeFiles.Inside(folder, pending.Id + "/" + i + ".original")));
            else if (current != entry.OriginalSha256) { conflicts.Add(entry.Path); continue; }
            if (SafeFiles.Hash(target) != entry.OriginalSha256) throw new IOException("Custom session restoration failed.");
            // File.Copy retained the original read-only flag in the backup. This
            // also repairs an interruption between clearing that flag and rename.
            var backup = SafeFiles.Inside(folder, pending.Id + "/" + i + ".original");
            SafeFiles.SetReadOnly(target, (File.GetAttributes(backup) & FileAttributes.ReadOnly) != 0);
        }
        if (conflicts.Count != 0) throw new IOException("External edits preserved; recovery remains pending: " + string.Join(", ", conflicts));
        File.Move(journal, SafeFiles.Inside(folder, pending.Id + "/recovered.json"));
        // Restoration is committed. Keep the small journal, release verified backups.
        for (int i = 0; i < pending.Files.Length; i++)
        {
            var backup = SafeFiles.Inside(folder, pending.Id + "/" + i + ".original");
            try { if (SafeFiles.Hash(backup) == pending.Files[i].OriginalSha256) SafeFiles.DeleteOwned(backup); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
