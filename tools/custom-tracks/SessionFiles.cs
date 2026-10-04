namespace DiRT2VR.CustomTracks;

// One journal covers all shared Aspen files, including optional night lighting.
// The fixed allowlist is compiled into the launcher, not supplied by a download.
public static class SessionFiles
{
    const string Folder = "DiRT2VR/custom-track-session";
    sealed record Entry(string Path, string OriginalSha256, string AppliedSha256);
    sealed record Pending(int Schema, string Id, Entry[] Files);
    public static void Prepare(string game, string layoutId)
    {
        SafeFiles.RequireClosed(); Recover(game);
        var pack = TrackPacks.ForLayout(layoutId);
        var receipt = pack.Read(game);
        var session = receipt.Sessions.Single(s => s.LayoutId == pack.GetLayout(layoutId).Id);
        var entries = session.Files.Select(f => new Entry(f.Path, f.OriginalSha256,
            receipt.Files.Single(p => p.Path == f.InstalledPath).Sha256)).ToArray();
        var id = Guid.NewGuid().ToString("N");
        var folder = SafeFiles.Inside(game, Folder);
        Directory.CreateDirectory(folder);
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i]; var target = SafeFiles.Inside(game, entry.Path);
            if (SafeFiles.Hash(target) != entry.OriginalSha256) throw new IOException("This track needs the original game file: " + entry.Path + ". External edits were preserved.");
            var backup = SafeFiles.Inside(folder, id + "/" + i + ".original");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(target, backup);
            if (SafeFiles.Hash(backup) != entry.OriginalSha256) throw new IOException("Session backup verification failed.");
        }
        SafeFiles.WriteJson(SafeFiles.Inside(folder, "pending.json"), new Pending(1, id, entries));
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i]; var target = SafeFiles.Inside(game, entry.Path);
            if (SafeFiles.Hash(target) != entry.OriginalSha256) throw new IOException("Game file changed during session preparation; backup retained.");
            var replacement = SafeFiles.Inside(game, session.Files[i].InstalledPath);
            var bytes = File.ReadAllBytes(replacement);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != entry.AppliedSha256) throw new IOException("Custom session file changed during preparation.");
            SafeFiles.Atomic(target, bytes);
        }
    }
    public static void Recover(string game)
    {
        SafeFiles.RequireClosed();
        var folder = SafeFiles.Inside(game, Folder);
        var journal = SafeFiles.Inside(folder, "pending.json");
        if (!File.Exists(journal)) return;
        var pending = SafeFiles.ReadJson<Pending>(journal);
        if (pending.Schema != 1 || !Guid.TryParseExact(pending.Id, "N", out _) || pending.Files is null || pending.Files.Length == 0 || pending.Files.Any(f => f is null) ||
            pending.Files.Select(f => f.Path).Distinct().Count() != pending.Files.Length ||
            pending.Files.Any(f => !TrackPacks.SessionTargets.Contains(f.Path) || !SafeFiles.Digest(f.OriginalSha256) || !SafeFiles.Digest(f.AppliedSha256)))
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
            try { if (SafeFiles.Hash(backup) == pending.Files[i].OriginalSha256) { SafeFiles.SetReadOnly(backup, false); File.Delete(backup); } }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
