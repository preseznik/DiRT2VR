namespace DiRT2VR.CustomTracks;

// Fixed per-pack destinations; receipt metadata cannot select a game-file target.
// The receipt is published last. Until then, recovery rolls all roots back.
public static class PackInstallation
{
    const string Journal = "DiRT2VR/custom-track-install/pending.json";
    sealed record Pending(int Schema, string Id, PackReceipt? Before, PackReceipt? After, bool[] Existed, string[]? Roots = null);
    static string Work(string game, string id) => SafeFiles.Inside(game, "DiRT2VR/custom-track-install/" + id);

    public static void Install(string game, string staging, string launcherVersion)
    {
        SafeFiles.RequireClosed(); Recover(game);
        SessionFiles.Recover(game);
        var pack = TrackPacks.FromStaging(staging);
        var next = pack.Read(staging);
        if (System.Version.Parse(next.MinimumLauncher) > System.Version.Parse(launcherVersion)) throw new IOException("Update DiRT2VR before installing this pack.");
        var before = Existing(game, pack);
        var installRoots = pack.ReceiptRoots(next);
        if (before is not null && pack.ReceiptLayouts(before).Length > pack.ReceiptLayouts(next).Length)
            throw new IOException("Uninstall the newer " + pack.Name + " pack before installing an older build with fewer layouts.");
        string id = Guid.NewGuid().ToString("N"), work = Work(game, id);
        var fresh = Path.Combine(work, "new");
        foreach (var file in next.Files)
        {
            SafeFiles.RequireClosed();
            var target = SafeFiles.Inside(fresh, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(SafeFiles.Inside(staging, file.Path), target, false);
        }
        pack.Verify(fresh, next);
        var pending = new Pending(1, id, before, next, installRoots.Select(p => Directory.Exists(SafeFiles.Inside(game, p))).ToArray(), installRoots);
        // Recheck ownership immediately before journaling/moving any live folders.
        CheckOwned(game, before, allowMissing: true, pack: pack);
        SafeFiles.RequireClosed();
        SafeFiles.WriteJson(SafeFiles.Inside(game, Journal), pending);
        try
        {
            foreach (var root in installRoots)
            {
                var target = SafeFiles.Inside(game, root);
                var backup = SafeFiles.Inside(work, "old/" + root);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (Directory.Exists(target)) Directory.Move(target, backup);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(SafeFiles.Inside(fresh, root), target);
            }
            pack.Verify(game, next);
            SafeFiles.WriteJson(SafeFiles.Inside(game, pack.Receipt), next);
            Finish(game, pending);
        }
        catch { Recover(game); throw; }
    }

    public static void Uninstall(string game, string packId = AspenPack.Id)
    {
        SafeFiles.RequireClosed(); Recover(game); SessionFiles.Recover(game);
        var pack = TrackPacks.Get(packId);
        var before = Existing(game, pack);
        if (before is null) return;
        string id = Guid.NewGuid().ToString("N"), work = Work(game, id);
        var pending = new Pending(1, id, before, null, pack.InstallRoots.Select(p => Directory.Exists(SafeFiles.Inside(game, p))).ToArray(), pack.InstallRoots);
        SafeFiles.WriteJson(SafeFiles.Inside(game, Journal), pending);
        try
        {
            foreach (var root in pack.InstallRoots)
            {
                var target = SafeFiles.Inside(game, root);
                var backup = SafeFiles.Inside(work, "old/" + root);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (Directory.Exists(target)) Directory.Move(target, backup);
            }
            // A committed marker distinguishes a completed uninstall from an interrupted one.
            SafeFiles.WriteJson(Path.Combine(work, "committed.json"), new { Schema = 1 });
            Finish(game, pending);
        }
        catch { Recover(game); throw; }
    }

    static PackReceipt? Existing(string game, TrackPack pack)
    {
        var receipt = File.Exists(SafeFiles.Inside(game, pack.Receipt)) ? pack.Read(game, false) : null;
        CheckOwned(game, receipt, allowMissing: true, pack: pack);
        return receipt;
    }
    internal static void CheckOwned(string game, PackReceipt? receipt, bool allowMissing, TrackPack? pack = null)
    {
        pack ??= receipt is null ? TrackPacks.Aspen : TrackPacks.Get(receipt.Id);
        var expected = receipt?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var root in pack.InstallRoots)
        {
            var path = SafeFiles.Inside(game, root);
            if (File.Exists(path) || (Directory.Exists(path) && receipt is null))
                throw new IOException("An existing folder is not owned by this installer: " + root + ". Preserve or move it before installing this pack.");
            if (!Directory.Exists(path)) continue;
            foreach (var file in SafeFiles.Tree(path))
            {
                string relative = Path.GetRelativePath(game, file).Replace('\\', '/');
                if (relative == pack.Receipt) continue;
                if (!expected.TryGetValue(relative, out var entry) || SafeFiles.Hash(file) != entry.Sha256)
                    throw new IOException("External changes were preserved: " + relative + ". Move your modified files aside before rebuilding or uninstalling.");
            }
        }
        if (!allowMissing && receipt is not null) pack.Verify(game, receipt);
    }

    public static void Recover(string game)
    {
        SafeFiles.RequireClosed();
        var journal = SafeFiles.Inside(game, Journal);
        if (!File.Exists(journal)) return;
        var pending = SafeFiles.ReadJson<Pending>(journal);
        if (pending.Before is null && pending.After is null) throw new IOException("Invalid installation journal; files preserved.");
        var pack = TrackPacks.Get((pending.After ?? pending.Before)!.Id);
        if (pending.Before is not null && pending.After is not null && pending.Before.Id != pending.After.Id) throw new IOException("Mixed-pack installation journal; files preserved.");
        if (pending.Before is not null) pack.Validate(pending.Before);
        if (pending.After is not null) pack.Validate(pending.After);
        // Old journals predate layout expansion and have no explicit root list.
        // The receipt version defines their exact compiled-in destinations.
        var receiptRoots = pack.ReceiptRoots((pending.After ?? pending.Before)!);
        var roots = pending.Roots ?? receiptRoots;
        if (!roots.SequenceEqual(pack.InstallRoots) && !roots.SequenceEqual(receiptRoots))
            throw new IOException("Unknown installation roots; files preserved.");
        if (pending.Schema != 1 || !Guid.TryParseExact(pending.Id, "N", out _) || pending.Existed is null || pending.Existed.Length != roots.Length ||
            (pending.Before is null && pending.After is null)) throw new IOException("Invalid installation journal; files preserved.");
        var work = Work(game, pending.Id);
        var receiptPath = SafeFiles.Inside(game, pack.Receipt);
        if (pending.After is not null && File.Exists(receiptPath) &&
            File.ReadAllBytes(receiptPath).SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(pending.After, SafeFiles.Json)))
        {
            // A same-version rebuild can leave an identical old receipt while
            // folders are still moving. Only a complete inventory proves commit.
            bool complete = false;
            try { pack.Verify(game, pending.After); complete = true; }
            catch (IOException) { /* The guarded rollback below checks every conflict. */ }
            if (complete) { Finish(game, pending); return; }
        }
        if (pending.After is null && File.Exists(Path.Combine(work, "committed.json"))) { Finish(game, pending); return; }
        // Preflight every root before rollback; never overwrite a conflicting external edit.
        for (int i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            var backup = SafeFiles.Inside(work, "old/" + root);
            var current = SafeFiles.Inside(game, root);
            if (Directory.Exists(backup)) CheckRoot(Path.Combine(work, "old"), root, pending.Before);
            if (Directory.Exists(current)) CheckRoot(game, root, Directory.Exists(backup) || !pending.Existed[i] ? pending.After : pending.Before);
            if (pending.Existed[i] && !Directory.Exists(backup) && !Directory.Exists(current)) throw new IOException("Installation backup is missing; files preserved.");
        }
        for (int i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            var backup = SafeFiles.Inside(work, "old/" + root);
            var current = SafeFiles.Inside(game, root);
            if (Directory.Exists(backup) || !pending.Existed[i])
            {
                if (Directory.Exists(current))
                {
                    var abandoned = SafeFiles.Inside(work, "interrupted/" + root);
                    Directory.CreateDirectory(Path.GetDirectoryName(abandoned)!);
                    Directory.Move(current, abandoned);
                }
                if (Directory.Exists(backup)) Directory.Move(backup, current);
            }
        }
        File.Move(journal, Path.Combine(work, "recovered.json"));
        Cleanup(work, "interrupted", pending.After);
        Cleanup(work, "new", pending.After);
    }
    static void CheckRoot(string game, string root, PackReceipt? receipt)
    {
        if (receipt is null) throw new IOException("Unowned installation files; recovery stopped.");
        var pack = TrackPacks.Get(receipt.Id);
        var expected = receipt.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var file in SafeFiles.Tree(SafeFiles.Inside(game, root)))
        {
            var relative = Path.GetRelativePath(game, file).Replace('\\', '/');
            if (relative == pack.Receipt)
            {
                if (!File.ReadAllBytes(file).SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(receipt, SafeFiles.Json)))
                    throw new IOException("Installation receipt changed externally; preserved.");
            }
            else if (!expected.TryGetValue(relative, out var entry) || SafeFiles.Hash(file) != entry.Sha256)
                throw new IOException("External changes preserved during installation recovery: " + relative);
        }
    }
    static void Finish(string game, Pending pending)
    {
        var work = Work(game, pending.Id);
        File.Move(SafeFiles.Inside(game, Journal), Path.Combine(work, "completed.json"));
        Cleanup(work, "old", pending.Before);
        Cleanup(work, "new", pending.After);
    }
    static void Cleanup(string work, string name, PackReceipt? receipt)
    {
        var root = SafeFiles.Inside(work, name);
        if (!Directory.Exists(root) || receipt is null) return;
        var pack = TrackPacks.Get(receipt.Id);
        try
        {
            foreach (var path in pack.InstallRoots)
                if (Directory.Exists(SafeFiles.Inside(root, path))) CheckRoot(root, path, receipt);
            SafeFiles.DeleteWorkTree(work, name);
        }
        catch (IOException) { /* Preserve any conflict or locked backup; the committed pack remains usable. */ }
        catch (UnauthorizedAccessException) { }
    }
}
