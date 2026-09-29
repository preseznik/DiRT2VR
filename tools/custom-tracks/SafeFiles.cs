using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiRT2VR.CustomTracks;

public static class SafeFiles
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 32 };
    public static bool Version(string value) => value is not null && Regex.IsMatch(value, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$");
    public static bool Digest(string value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
    public static void Relative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 220 || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') ||
                p.Any(c => c < 32 || "<>\"|?*".Contains(c)) || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase)))
            throw new IOException("Unsafe package path: " + path);
    }
    public static void NoLinks(string path)
    {
        for (var cursor = Path.GetFullPath(path); !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files and folders are unsupported: " + cursor);
    }
    public static string Inside(string root, string relative)
    {
        Relative(relative);
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Package path escapes its destination.");
        NoLinks(path);
        return path;
    }
    public static string Hash(string path)
    {
        NoLinks(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static T ReadJson<T>(string path)
    {
        NoLinks(path);
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Manifest is too large.");
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new IOException("Manifest is empty."); }
        catch (JsonException e) { throw new IOException("Manifest is invalid.", e); }
    }
    public static void WriteJson<T>(string path, T value) => Atomic(path, JsonSerializer.SerializeToUtf8Bytes(value, Json));
    public static void Atomic(string path, byte[] bytes)
    {
        NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
    public static IEnumerable<string> Tree(string root)
    {
        NoLinks(root);
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            NoLinks(path);
            if (Directory.Exists(path)) { foreach (var child in Tree(path)) yield return child; }
            else yield return path;
        }
    }
    public static void RequireClosed()
    {
        foreach (var name in new[] { "dirt2", "dirt2_game" })
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            bool running = processes.Length != 0;
            foreach (var process in processes) process.Dispose();
            if (running) throw new IOException("Close DiRT 2 before installing, rebuilding or changing custom tracks.");
        }
    }
    public static void DeleteWorkTree(string parent, string relative)
    {
        // Only call for this operation's private staging directory, after its
        // process has exited. Resolve and reject links before deleting anything.
        string root = Inside(parent, relative);
        if (!Directory.Exists(root)) return;
        var files = Tree(root).ToArray();
        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length).ToArray();
        foreach (var directory in directories) NoLinks(directory);
        foreach (var file in files)
        {
            // Older converters copied read-only source flags into their private
            // work directories. Only clear that flag inside the validated tree.
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            File.Delete(file);
        }
        foreach (var directory in directories) Directory.Delete(directory);
        Directory.Delete(root);
    }
}
