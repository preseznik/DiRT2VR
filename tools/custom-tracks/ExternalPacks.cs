using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DiRT2VR.CustomTracks;

public sealed record ExternalFile(string Path, long Bytes, string Sha256);
public sealed record ExternalDependency(string Path, string Sha256);
public sealed record ExternalReplacement(string Target, string File);
public sealed record ExternalLayout(string Id, string Name, string Folder, string Condition,
    string Discipline, bool Circuit, int MaximumOpponents, ExternalReplacement[] Session);
public sealed record ExternalCar(string Code, string Donor, string ClassName);
public sealed record ExternalManifest(int Schema, string Kind, string Id, string Name, string Author,
    string Version, string MinimumLauncher, string Description, string[] Modes, bool Replay,
    ExternalFile[] Files, ExternalDependency[] Dependencies, ExternalLayout[] Layouts, ExternalCar? Car);

// Immutable discovery result. No executable plugins, source conversion or writes during discovery.
public sealed class ExternalPack
{
    public string Root { get; }
    public string Kind { get; }
    public string FolderId { get; }
    public string Id => "external-" + Kind + "-" + FolderId;
    public string Name => Manifest?.Name is string name && !string.IsNullOrWhiteSpace(name) && name.Length <= 100 && !name.Any(char.IsControl) ? name : FolderId;
    public string Problem { get; internal set; }
    public ExternalManifest? Manifest { get; }
    public string ManifestHash { get; }
    public bool Ready => Problem.Length == 0;
    public string Support => $"DiRT2VR/custom-{Kind}s/external/{FolderId}";
    public string Receipt => Support + "/manifest.json";
    internal ExternalPack(string root, string kind, string id, ExternalManifest? manifest, string hash, string problem)
        => (Root, Kind, FolderId, Manifest, ManifestHash, Problem) = (root, kind, id, manifest, hash, problem);
    public string LayoutId(ExternalLayout layout) => Id + "/" + layout.Id;
    public ExternalLayout Layout(string id) => Manifest!.Layouts.SingleOrDefault(l => LayoutId(l) == id)
        ?? throw new IOException("External layout is not present in this pack.");
    public void Verify(CancellationToken token = default)
    {
        if (!Ready) throw new IOException(Problem);
        if (SafeFiles.Hash(SafeFiles.Inside(Root, "manifest.json"), token) != ManifestHash)
            throw new IOException("External manifest changed. Refresh the content list before launching.");
        foreach (var file in Manifest!.Files)
        {
            string path = SafeFiles.Inside(Root, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes || SafeFiles.Hash(path, token) != file.Sha256)
                throw new IOException("External pack file is missing or changed: " + file.Path + ". Replace the complete pack, then Refresh.");
        }
    }
    public void RequireDependencies(string game, CancellationToken token = default)
    {
        foreach (var dependency in Manifest!.Dependencies)
        {
            string path = SafeFiles.Inside(game, dependency.Path);
            if (!File.Exists(path) || SafeFiles.Hash(path, token) != dependency.Sha256)
                throw new IOException("External pack needs a different or missing game dependency: " + dependency.Path);
        }
    }
    internal TrackPack TrackDefinition() => new(Id, Name, Manifest!.Version, Manifest.MinimumLauncher, Manifest.Modes,
        Manifest.Layouts.Select(l => new Layout(LayoutId(l), l.Name, l.Folder, l.Condition, l.Discipline, l.Circuit)).ToArray(), [], this);
    internal PackReceipt ReadReceipt(bool verify, CancellationToken token)
    {
        if (verify) Verify(token);
        return new(2, Id, Manifest!.Version, Manifest.MinimumLauncher,
            Manifest.Files.Select(f => new PackFile(Support + "/" + f.Path, f.Bytes, f.Sha256)).ToArray(),
            Manifest.Layouts.Select(l => new LayoutSession(LayoutId(l), [], Manifest.Version)).ToArray(), []);
    }
}

public static class ExternalPacks
{
    // Reserve stock and supported codes even when their files are absent.
    public static readonly string[] StockCarCodes = ["350", "35t", "35x", "bmt", "bmw", "bmx", "bow", "cme", "cr4", "crt", "crx", "dej", "drt", "e9r", "e9t", "e9x", "ecl", "ect", "ecx", "hh3", "hhx", "hon", "hsb", "ick", "kin", "lan", "mer", "met", "mex", "n12", "n1t", "n1x", "pfl", "por", "prc", "sor", "sot", "sox", "sti", "stt", "stx", "tfj", "vwt"];
    public static bool AvailableCarCode(string code) => Regex.IsMatch(code, "^[a-z0-9]{3}$") && code != "f04" && !StockCarCodes.Contains(code);
    internal static bool CarAssetPath(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 3 && parts[0] is "cars" or "interiors" && AvailableCarCode(parts[1]) && DataExtension(path);
    }
    public static string RelativeFolder(string kind) => kind is "track" or "car"
        ? "DiRT2VR/custom-" + kind + "s/external" : throw new IOException("Unknown external content kind.");
    static readonly JsonSerializerOptions Json = new(SafeFiles.Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    static bool Slug(string? id) => id is not null && Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,47}$");
    internal static bool AssetPath(string path) => Regex.IsMatch(path, "^tracks/usa/d2ve_[a-z0-9_]{1,48}/") && DataExtension(path);
    static bool DataExtension(string path) => new[] { ".xml", ".bin", ".pssg", ".ens", ".jpk", ".vis", ".clm", ".grs", ".cqtc", ".cns", ".txt", ".lng", ".htf", ".nfs", ".gssp", ".ctf", ".nd2", ".ini", ".dic", ".wip", ".ppts" }.Contains(Path.GetExtension(path));
    public static ExternalPack[] Discover(string game, string launcherVersion)
    {
        var results = new List<ExternalPack>();
        foreach (string kind in new[] { "track", "car" })
        {
            string parent = Path.Combine(game, RelativeFolder(kind));
            string[] folders;
            try
            {
                SafeFiles.NoLinks(parent);
                if (!Directory.Exists(parent)) continue;
                folders = Directory.EnumerateDirectories(parent).Order(StringComparer.OrdinalIgnoreCase).Take(257).ToArray();
                if (folders.Length > 256) throw new IOException("Too many external packs. Keep at most 256 packs in each external folder.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new(parent, kind, "folder-unavailable", null, "", "Cannot scan external " + kind + " folder: " + ex.Message));
                continue;
            }
            foreach (string folder in folders)
            {
                string id = Path.GetFileName(folder); ExternalManifest? manifest = null; string hash = "", problem = "";
                try
                {
                    SafeFiles.NoLinks(folder);
                    if (!Slug(id)) throw new IOException("Use a lowercase pack folder name containing letters, digits and hyphens.");
                    string path = SafeFiles.Inside(folder, "manifest.json");
                    if (!File.Exists(path)) throw new IOException("Missing manifest.json. Place a complete prepared DiRT2VR pack in this folder.");
                    if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException("External manifest exceeds 2 MiB.");
                    byte[] bytes = File.ReadAllBytes(path);
                    manifest = JsonSerializer.Deserialize<ExternalManifest>(bytes, Json) ?? throw new IOException("Empty external manifest.");
                    hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                    Validate(manifest, kind, id, launcherVersion);
                    manifest = manifest with {
                        Files = manifest.Files.Select(f => f with { Sha256 = f.Sha256.ToUpperInvariant() }).ToArray(),
                        Dependencies = manifest.Dependencies.Select(d => d with { Sha256 = d.Sha256.ToUpperInvariant() }).ToArray()
                    };
                    foreach (var file in manifest.Files)
                    {
                        string asset = SafeFiles.Inside(folder, file.Path);
                        if (!File.Exists(asset) || new FileInfo(asset).Length != file.Bytes) throw new IOException("Missing or incomplete pack file: " + file.Path);
                    }
                    if (kind == "car" && new[] { "cars/", "interiors/" }.Any(prefix => { string target = SafeFiles.Inside(game, prefix + manifest.Car!.Code); return Directory.Exists(target) && SafeFiles.Tree(target).Any(); }))
                        problem = "This car code is already used by an installed car. The external pack cannot replace it.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or OverflowException)
                { problem = ex.Message; }
                results.Add(new(folder, kind, id, manifest, hash, problem));
            }
        }
        // Neither copy wins a collision. No scan order or overwrite behavior decides it.
        foreach (var group in results.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var pack in group) pack.Problem = "Duplicate external pack identity. Keep one copy, then Refresh.";
        var claimed = new Dictionary<string, List<ExternalPack>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in results.Where(p => p.Ready && p.Kind == "track"))
            foreach (var layout in pack.Manifest!.Layouts)
            {
                if (!claimed.TryGetValue(layout.Folder, out var owners)) claimed[layout.Folder] = owners = [];
                owners.Add(pack);
            }
        foreach (var owners in claimed.Values.Where(v => v.Count > 1))
            foreach (var pack in owners) pack.Problem = "Another external pack claims the same track folder. Remove the conflicting copy, then Refresh.";
        foreach (var group in results.Where(p => p.Kind == "car" && p.Ready)
            .GroupBy(p => p.Manifest!.Car!.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var pack in group) pack.Problem = "Another external car pack claims the same car code. Remove the conflicting copy, then Refresh.";
        return results.ToArray();
    }
    public static void Validate(ExternalManifest m, string kind, string folderId, string launcher)
    {
        static bool Text(string? s, int maximum, bool empty = false) => s is not null && (empty || !string.IsNullOrWhiteSpace(s)) && s.Length <= maximum && !s.Any(char.IsControl);
        if (m.Schema != 1 || m.Kind != kind || !Slug(m.Id) || m.Id != folderId || m.Id == "f04" || TrackPacks.BuiltIn.Any(p => p.Id == m.Id) ||
            !Text(m.Name, 100) || !Text(m.Author, 100) || !Text(m.Description, 1000, true) || !SafeFiles.Version(m.Version) || !SafeFiles.Version(m.MinimumLauncher))
            throw new IOException("Unsupported external manifest identity or format.");
        if (System.Version.Parse(m.MinimumLauncher) > System.Version.Parse(launcher)) throw new IOException("Update DiRT2VR to " + m.MinimumLauncher + " or later for this pack.");
        if (m.Modes is null || m.Modes.Length is < 1 or > 4 || m.Modes.Distinct().Count() != m.Modes.Length || m.Modes.Any(x => !new[] { "desktop-solo", "desktop-race", "vr-solo", "vr-race" }.Contains(x)))
            throw new IOException("Unsupported external launch modes.");
        if (m.Modes.Any(mode => mode.StartsWith("vr-", StringComparison.Ordinal) && !m.Modes.Contains("desktop-" + mode[3..])))
            throw new IOException("A VR mode also requires its corresponding desktop mode.");
        if (!m.Modes.Contains("desktop-solo")) throw new IOException("External packs must support desktop Direct practice before enabling other modes.");
        if (m.Files is null || m.Files.Length is < 1 or > 4096 || m.Files.Any(f => f is null || !SafeFiles.Digest(f.Sha256) || f.Bytes is < 1 or > 805306368) ||
            m.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Files.Length || m.Files.Sum(f => f.Bytes) > 4L * 1024 * 1024 * 1024)
            throw new IOException("Invalid external file inventory.");
        foreach (var file in m.Files)
        {
            SafeFiles.Relative(file.Path);
            if (!file.Path.StartsWith("payload/", StringComparison.Ordinal) || !DataExtension(file.Path)) throw new IOException("Only prepared game data is allowed in external payloads.");
        }
        if (m.Dependencies is null || m.Dependencies.Length > 512 || m.Dependencies.Any(d => d is null || !SafeFiles.Digest(d.Sha256)) ||
            m.Dependencies.Select(d => d.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Dependencies.Length) throw new IOException("Invalid external dependencies.");
        foreach (var dependency in m.Dependencies) SafeFiles.Relative(dependency.Path);
        if (kind == "car")
        {
            if (m.Car is null || !AvailableCarCode(m.Car.Code ?? "") || !StockCarCodes.Contains(m.Car.Donor) || m.Car.Code == m.Car.Donor || !Text(m.Car.ClassName, 80) || m.Layouts is null || m.Layouts.Length != 0)
                throw new IOException("Invalid external car description.");
            string[] roots = [$"payload/cars/{m.Car.Code}/", $"payload/interiors/{m.Car.Code}/"];
            if (m.Files.Any(f => !roots.Any(root => f.Path.StartsWith(root, StringComparison.Ordinal))))
                throw new IOException("External car payload is outside its own car or interior folders.");
            foreach (string required in new[] { $"cars/{m.Car.Code}/cameras.xml", $"cars/{m.Car.Code}/{m.Car.Code}.ctf", $"cars/{m.Car.Code}/{m.Car.Code}.nd2", $"cars/{m.Car.Code}/{m.Car.Code}_highLOD.pssg", $"cars/{m.Car.Code}/{m.Car.Code}_lowLOD.pssg", $"interiors/{m.Car.Code}/int_{m.Car.Code}.pssg", $"interiors/{m.Car.Code}/int_{m.Car.Code}.xml" })
                if (!m.Files.Any(f => f.Path == "payload/" + required)) throw new IOException("Incomplete external car: " + required);
            foreach (string required in new[] { "database/database.bin", "cars/class_mappings.xml", "audio/carsetup.bin", "audio/carsetup_ai.bin", "audio/dictionaries.bin" })
                if (!m.Dependencies.Any(d => d.Path == required)) throw new IOException("External car needs a pinned registration dependency: " + required);
            return;
        }
        if (m.Car is not null || m.Layouts is null || m.Layouts.Length is < 1 or > 32 || m.Layouts.Any(l => l is null) ||
            m.Layouts.Select(l => l.Id).Distinct().Count() != m.Layouts.Length || m.Layouts.Select(l => l.Folder).Distinct().Count() != m.Layouts.Length)
            throw new IOException("Invalid external layout list.");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layout in m.Layouts)
        {
            if (!Slug(layout.Id) || !Text(layout.Name, 100) || !Regex.IsMatch(layout.Folder ?? "", "^d2ve_[a-z0-9_]{1,48}$") || !Text(layout.Condition, 80) ||
                !new[] { "Rally", "Rallycross", "Landrush", "Trailblazer", "Head-to-head", "Point-to-point" }.Contains(layout.Discipline) || layout.MaximumOpponents is < 0 or > 7 ||
                (m.Modes.Any(x => x.EndsWith("-race")) && layout.MaximumOpponents == 0)) throw new IOException("Invalid external layout or starting grid.");
            string prefix = $"payload/tracks/usa/{layout.Folder}/";
            foreach (var f in m.Files.Where(f => f.Path.StartsWith(prefix, StringComparison.Ordinal))) used.Add(f.Path);
            foreach (string required in new[] { "track.jpk", "routesplit.pssg", "track.vis", "grids.pssg", "progress_track.xml" })
                if (!used.Contains(prefix + "route_0/" + required)) throw new IOException("Incomplete external route: " + required);
            if (layout.Session is null || layout.Session.Length is < 1 or > 32 || layout.Session.Any(s => s is null) || layout.Session.Select(s => s.Target).Distinct().Count() != layout.Session.Length)
                throw new IOException("Invalid external session replacements.");
            foreach (var replace in layout.Session)
            {
                if (!TrackPacks.SessionTargets.Contains(replace.Target) || !m.Dependencies.Any(d => d.Path == replace.Target) ||
                    !m.Files.Any(f => f.Path == replace.File && f.Bytes <= 64 * 1024 * 1024) || !replace.File.StartsWith("payload/session/", StringComparison.Ordinal))
                    throw new IOException("External session replacement is outside supported game targets.");
                used.Add(replace.File);
            }
            if (!layout.Session.Any(s => s.Target == "database/database.bin")) throw new IOException("External track needs its prepared database registration.");
        }
        if (used.Count != m.Files.Length) throw new IOException("External inventory contains unreferenced files or unsupported destinations.");
    }
}
