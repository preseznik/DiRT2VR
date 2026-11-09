using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DiRT2VR.Profiles;

namespace DiRT2VR.Multiplayer;

internal sealed record Asset(string path, long size, string sha256);

internal static class PrivateGame
{
    internal static void PlainPath(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Direct cannot use a linked file or folder: " + p);
    }

    internal static string Hash(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f));
    }

    internal static List<Asset> StockAssets()
    {
        using var packed = typeof(PrivateGame).Assembly.GetManifestResourceStream("DirectStockAssets")!;
        using var plain = new GZipStream(packed, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<List<Asset>>(plain)!;
    }

    internal static string Prepare(string install, string cache, Action<string> status, CancellationToken cancel)
    {
        var root = Path.Combine(cache, "game");
        PlainPath(root);
        Directory.CreateDirectory(root);
        PrivateVrRenderer.Recover(cache);
        PrivateCars.Recover(cache);
        PrivateCircuit.Recover(cache);
        var assets = StockAssets();
        int wrapperIndex = assets.FindIndex(a => a.path == "dirt2.exe");
        var wrapper = Path.Combine(install, "dirt2.exe");
        PlainPath(wrapper);
        var wrapperHash = Hash(wrapper);
        if (wrapperHash == "9D0F5716CBD0235155C638119E0C35607E6FD00CFDB97E4D7F9DC1091F3DB618")
        {
            assets[wrapperIndex] = new("dirt2.exe", 73728, wrapperHash);
            assets.Add(new("dirt2o.exe", 6055256, "7FC1077FBFCEA64236A4FDE0D76058A469960D7D2269C2C0834E0902A9E67E49"));
            assets.Add(new("rld.dll", 91648, "9BBC2FE0CA23C404357A985300CBFEFBAD6ADCD7DEA091F5B4CEBE36163E5484"));
        }
        else if (!string.Equals(wrapperHash, assets[wrapperIndex].sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unsupported dirt2.exe for Direct. SHA-256 " + wrapperHash);

        var allowed = assets.Select(a => Path.GetFullPath(Path.Combine(root,a.path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        allowed.Add(Path.Combine(root,"xlive.dll"));
        var dirs = new Stack<DirectoryInfo>();
        dirs.Push(new DirectoryInfo(root));
        while (dirs.TryPop(out var dir))
        {
            cancel.ThrowIfCancellationRequested();
            PlainPath(dir.FullName);
            // The root and every parent directory have already been checked.
            // Windows returns type and link attributes with this directory entry.
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Direct cannot use a linked file or folder: " + entry.FullName);
                if (entry is DirectoryInfo child) dirs.Push(child);
                else if (!allowed.Contains(entry.FullName)) throw new IOException("Unexpected file in the Direct cache: " + entry.FullName);
            }
        }

        var staging = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            for (int i = 0; i < assets.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var a = assets[i];
                if (i % 250 == 0) status($"Preparing game files ({i:N0} / {assets.Count:N0})…");
                string to = Path.Combine(root,a.path), from = Path.Combine(install,a.path);
                // A previously verified private copy is independently hash-checked on reuse.
                if (File.Exists(to) && new FileInfo(to).Length == a.size && Hash(to).Equals(a.sha256,StringComparison.OrdinalIgnoreCase)) continue;
                PlainPath(from);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (a.path is "tracks/baja/baja_iron/track_light_anims.pssg" or "tracks/london/battersea/track_light_anims.pssg" or "tracks/usa/la_stadium/track_light_anims.pssg"
                    && new FileInfo(from).Length < a.size)
                {
                    byte[] bytes = File.ReadAllBytes(from);
                    using var output = File.Create(staging);
                    for (int j = 0; j < bytes.Length; j++)
                    {
                        if (bytes[j] == 10 && (j == 0 || bytes[j-1] != 13)) output.WriteByte(13);
                        output.WriteByte(bytes[j]);
                    }
                }
                else File.Copy(from,staging,true);
                if (new FileInfo(staging).Length != a.size || !Hash(staging).Equals(a.sha256,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Direct needs the stock game file " + a.path + ". Found SHA-256 " + Hash(from));
                File.Move(staging,to,true);
            }
        }
        finally { File.Delete(staging); }
        return root;
    }

    internal static string Snapshot(string userRoot, string run)
    {
        var store = new ProfileStore(Path.Combine(Path.GetDirectoryName(userRoot)!, "profiles"));
        var id = store.LoadSelection(Path.Combine(userRoot,"profile-selection.json"));
        string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"My Games","DiRT2");
        string root = docs;
        if (id != ProfileStore.CurrentCareer) { store.Read(id); root = store.ProfileRoot(id); }
        var source = Path.Combine(root,"savegame");
        PlainPath(source);
        var folders = new Stack<string>();
        folders.Push(source);
        int files = 0;
        long total = 0;
        while (folders.TryPop(out var dir))
            foreach (var file in Directory.EnumerateFileSystemEntries(dir))
            {
                PlainPath(file);
                if (Directory.Exists(file)) { folders.Push(file); continue; }
                long length = new FileInfo(file).Length;
                if (++files > 512 || length > Dirt2SaveContainer.MaxBytes || (total += length) > 64*1024*1024)
                    throw new IOException("Unexpected career size.");
                byte[] bytes = File.ReadAllBytes(file);
                Dirt2SaveContainer.Decode(bytes);
                var dest = Path.Combine(run, "career", Path.GetRelativePath(root,file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest,bytes);
            }
        var name = ProfileDetails.Inspect(Path.Combine(run,"career","savegame","Autosave0")).Name;
        if (files < 21 || string.IsNullOrWhiteSpace(name) || name.Length > 63 || name.Any(c => c < 32 || c > 126))
            throw new IOException("Select a readable existing career before starting Direct.");
        var hardware = Path.Combine(run,"Documents","My Games","DiRT2","hardwaresettings");
        Directory.CreateDirectory(hardware);
        foreach (var file in new[]{"hardware_settings_config.xml","hardware_settings_info.xml"})
        {
            var src = Path.Combine(docs,"hardwaresettings",file);
            PlainPath(src);
            if (File.Exists(src)) File.Copy(src,Path.Combine(hardware,file));
        }
        return name;
    }
}
