using System.Text.Json;
namespace DiRT2VR.Profiles;

public sealed record ProfileInfo(int Version, string Id, string Name, string Kind, DateTime CreatedUtc);
public sealed record ProfileSelection(int Version, string Id);
public sealed record ProfileEntry(string Id, ProfileInfo? Info, string? Error);

// The launcher supplies one shared per-user root and a separate installation selection.
public sealed class ProfileStore(string root)
{
    readonly string root = Path.GetFullPath(root);
    public const string CurrentCareer = "current";
    static void NoLinks(string path)
    {
        for (string? item = Path.GetFullPath(path); item != null; item = Path.GetDirectoryName(item))
            if (Path.Exists(item) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Profile storage cannot use a linked folder.");
    }
    static void ValidId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var parsed) || parsed.ToString("N") != id)
            throw new InvalidDataException("Invalid profile identifier.");
    }
    internal static string ValidName(string name)
    {
        if (name == null) throw new ArgumentException("A profile name is required.");
        name = name.Trim();
        if (name.Length is < 1 or > 24 || name.Any(char.IsControl) || name.Any(char.IsSurrogate))
            throw new ArgumentException("Use a profile name of 1 to 24 characters.");
        return name;
    }
    static T ReadJson<T>(string path)
    {
        NoLinks(path);
        if (new FileInfo(path).Length > 8192) throw new InvalidDataException("Profile metadata is too large.");
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Missing profile metadata.");
    }
    static void NewFile(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(true);
    }
    static void AtomicJson<T>(string path, T value)
    {
        NoLinks(path);
        var parent = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(parent);
        var temp = Path.Combine(parent, ".profile-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { NewFile(temp, JsonSerializer.SerializeToUtf8Bytes(value)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public string ProfileRoot(string id)
    {
        ValidId(id);
        var path = Path.Combine(root, id); NoLinks(path); return path;
    }
    public ProfileInfo Read(string id)
    {
        var path = ProfileRoot(id);
        var info = ReadJson<ProfileInfo>(Path.Combine(path, "profile.json"));
        if (info.Version != 1 || info.Id != id || info.Kind is not ("fresh" or "completed") || ValidName(info.Name) != info.Name)
            throw new InvalidDataException("This profile is incompatible.");
        foreach (var name in ProfileFactory.Files)
        {
            var record = Path.Combine(path, "savegame", "Autosave0", name); NoLinks(record);
            if (!File.Exists(record)) throw new IOException("The selected profile is incomplete.");
            if (new FileInfo(record).Length > Dirt2SaveContainer.MaxBytes) throw new InvalidDataException("Profile data is too large.");
            Dirt2SaveContainer.Decode(File.ReadAllBytes(record));
        }
        return info;
    }
    public IReadOnlyList<ProfileEntry> List()
    {
        NoLinks(root);
        if (!Directory.Exists(root)) return [];
        var entries = new List<ProfileEntry>();
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(path);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try { entries.Add(new ProfileEntry(id, Read(id), null)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException)
            { entries.Add(new ProfileEntry(id, null, ex.Message)); }
        }
        return entries.OrderBy(entry => entry.Info?.Name ?? entry.Id, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    public string LoadSelection(string preferences)
    {
        if (!File.Exists(preferences)) return CurrentCareer;
        var value = ReadJson<ProfileSelection>(preferences);
        if (value.Version != 1) throw new InvalidDataException("Unsupported profile selection version.");
        if (value.Id != CurrentCareer) Read(value.Id);
        return value.Id;
    }
    public void Select(string preferences, string id, Action requireClosed)
    {
        using var guard = new EditGuard(requireClosed);
        if (id != CurrentCareer) Read(id);
        AtomicJson(preferences, new ProfileSelection(1, id));
    }
    public ProfileInfo Create(string name, bool completed, Action requireClosed, Action<string>? checkpoint = null)
    {
        using var guard = new EditGuard(requireClosed);
        name = ValidName(name);
        NoLinks(root); Directory.CreateDirectory(root);
        var id = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, ".creating-" + id);
        var destination = ProfileRoot(id);
        var records = ProfileFactory.Build(name, completed);
        var created = new List<string>();
        var info = new ProfileInfo(1, id, name, completed ? "completed" : "fresh", DateTime.UtcNow);
        var save = Path.Combine(staging, "savegame", "Autosave0");
        try
        {
            Directory.CreateDirectory(save);
            foreach (var (file, bytes) in records)
            {
                var target = Path.Combine(save, file);
                created.Add(target); NewFile(target, bytes);
                Dirt2SaveContainer.Decode(File.ReadAllBytes(target)); checkpoint?.Invoke(file);
            }
            var metadata = Path.Combine(staging, "profile.json");
            created.Add(metadata); NewFile(metadata, JsonSerializer.SerializeToUtf8Bytes(info));
            checkpoint?.Invoke("before-register");
            Directory.Move(staging, destination);
            return info;
        }
        catch
        {
            // Only paths created here; never recursively remove a profile directory.
            foreach (var file in created) { NoLinks(file); if (File.Exists(file)) File.Delete(file); }
            if (Directory.Exists(save)) Directory.Delete(save);
            if (Directory.Exists(Path.Combine(staging, "savegame"))) Directory.Delete(Path.Combine(staging, "savegame"));
            if (Directory.Exists(staging)) Directory.Delete(staging);
            throw;
        }
    }

    sealed class EditGuard : IDisposable
    {
        readonly Mutex mutex = new(false, @"Global\DiRT2VR.Session");
        bool held;
        public EditGuard(Action requireClosed)
        {
            try
            {
                try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new IOException("Close the active DiRT2VR session before changing profiles.");
                requireClosed();
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (held) { mutex.ReleaseMutex(); held = false; }
            mutex.Dispose();
        }
    }
}
