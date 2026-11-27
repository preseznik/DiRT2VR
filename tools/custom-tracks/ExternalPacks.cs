using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DiRT2VR.CustomTracks;

public sealed record ExternalFile(string Path, long Bytes, string Sha256);
public sealed record ExternalDependency(string Path, string Sha256);
public sealed record ExternalReplacement(string Target, string File);
// A stock game file copied from the player's own installation into the layout's track folder for the
// session, so a pack can use venue resources without redistributing them. Source must be a pinned dependency.
public sealed record ExternalBorrow(string Target, string Source);
// Bytes [Offset, Offset + Length) of a pinned stock Source copied over the same range of one of the layout's own
// packed files for the session (e.g. a grass file's type library), so the pack ships that range blank.
public sealed record ExternalSplice(string Target, string Source, long Offset, int Length);
// A lighting choice for one layout: Lighting sets parameters of the layout's borrowed lighting.xml for the session,
// and Borrow takes some of the layout's borrowed files from other pinned stock files (e.g. another venue's sky.pssg).
// The first condition keeps the layout's own id; each other one is listed as its own layout, <layout>-<condition>,
// over the same installed files (its own best times and replays).
public sealed record ExternalCondition(string Id, string Name, Dictionary<string, string>? Lighting = null, ExternalBorrow[]? Borrow = null);
public sealed record ExternalLayout(string Id, string Name, string Folder, string Condition,
    string Discipline, bool Circuit, int MaximumOpponents, ExternalReplacement[] Session,
    string RaceStart = "grid", ExternalReplacement[]? RaceSession = null,
    int GridMaximumOpponents = 0, ExternalReplacement[]? GridRaceSession = null, ExternalBorrow[]? Borrow = null,
    ExternalRegistration? Registration = null, ExternalSplice[]? Splice = null, ExternalCondition[]? Conditions = null,
    ExternalReplacement[]? VrSession = null);
// VrSession: packed layout files replaced by lighter versions in VR sessions only (desktop keeps the originals).
// Optional own engine sound: DiRT 2 ramp-engine samples in payload/audio/dictionaries/d2ve_<code>/
// (NESSamples.dic and <Bank>.wip) played through payload/audio/cars/<code>engine.ini.
// Samples is the ramp length in frames; Turbo false silences the donor's turbo layer.
public sealed record ExternalCarAudio(string Bank, int Samples, bool Turbo);
// Optional livery names in order: entry k is payload/cars/<code>/livery_<kk>/ (ExternalPacks.LiveryFiles).
// Absent means one livery, livery_00, as before.
public sealed record ExternalCar(string Code, string Donor, string ClassName, ExternalCarAudio? Audio = null, string[]? Liveries = null);
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
    internal bool BelongsToInstallation(string game) =>
        StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(Root), SafeFiles.Inside(game, Support));
    internal ExternalPack(string root, string kind, string id, ExternalManifest? manifest, string hash, string problem)
        => (Root, Kind, FolderId, Manifest, ManifestHash, Problem) = (root, kind, id, manifest, hash, problem);
    public string LayoutId(ExternalLayout layout) => Id + "/" + layout.Id;
    // Every listed layout: each manifest layout once per condition (the first under the layout's own id).
    public IEnumerable<(string Id, ExternalLayout Layout, ExternalCondition? Condition)> Variants() =>
        Manifest!.Layouts.SelectMany(l => l.Conditions is { Length: > 0 } conditions
            ? conditions.Select((c, i) => (i == 0 ? LayoutId(l) : LayoutId(l) + "-" + c.Id, l with { Condition = c.Name }, (ExternalCondition?)c))
            : [(LayoutId(l), l, (ExternalCondition?)null)]);
    public ExternalLayout Layout(string id) => Variant(id).Layout;
    public ExternalCondition? Condition(string id) => Variant(id).Condition;
    (string Id, ExternalLayout Layout, ExternalCondition? Condition) Variant(string id)
    {
        foreach (var variant in Variants()) if (variant.Id == id) return variant;
        throw new IOException("External layout is not present in this pack.");
    }
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
        Variants().Select(v => new Layout(v.Id, v.Layout.Name, v.Layout.Folder, v.Layout.Condition, v.Layout.Discipline, v.Layout.Circuit)).ToArray(), [], this);
    internal PackReceipt ReadReceipt(bool verify, CancellationToken token)
    {
        if (verify) Verify(token);
        return new(2, Id, Manifest!.Version, Manifest.MinimumLauncher,
            Manifest.Files.Select(f => new PackFile(Support + "/" + f.Path, f.Bytes, f.Sha256)).ToArray(),
            Variants().Select(v => new LayoutSession(v.Id, [], Manifest.Version)).ToArray(), []);
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
        if (parts.Length >= 3 && parts[0] is "cars" or "interiors" && AvailableCarCode(parts[1]) && DataExtension(path)) return true;
        return CarAudioPath(path);
    }
    // audio/cars/<code>engine.ini files the game ships, including codes that are not stock car codes.
    static readonly string[] StockEngineAudioCodes = ["111", "350", "bmw", "bow", "cr4", "dej", "drb", "drt", "e9r", "ecl", "hh3", "hon", "hsb", "ick", "kin", "lan", "mer", "n12", "p92", "pfl", "por", "prc", "sor", "sti", "tfj", "tst"];
    public static string CarAudioFolder(string code) => "audio/dictionaries/d2ve_" + code;
    public const int MaximumLiveries = 16;
    // The textures of an external car's livery k, laid out as the stock cars' (the game reads them by the livery's file_string <code>_<kk>).
    public static string[] LiveryFiles(string code, int livery)
    {
        string number = livery.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        return [$"cars/{code}/livery_{number}/textures_high/{code}_tex_high_{number}.pssg", $"cars/{code}/livery_{number}/textures_low/{code}_tex_low_{number}.pssg"];
    }
    public static string CarEngineIni(string code) => "audio/cars/" + code + "engine.ini";
    static bool CarAudioPath(string path)
    {
        var match = Regex.Match(path, @"^audio/dictionaries/d2ve_([a-z0-9]{3})/[A-Za-z0-9_]{1,15}\.(dic|wip)$");
        if (match.Success) return AvailableCarCode(match.Groups[1].Value);
        match = Regex.Match(path, @"^audio/cars/([a-z0-9]{3})engine\.ini$");
        return match.Success && AvailableCarCode(match.Groups[1].Value) && !StockEngineAudioCodes.Contains(match.Groups[1].Value);
    }
    public static string RelativeFolder(string kind) => kind is "track" or "car"
        ? "DiRT2VR/custom-" + kind + "s/external" : throw new IOException("Unknown external content kind.");
    static readonly JsonSerializerOptions Json = new(SafeFiles.Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    static bool Slug(string? id) => id is not null && Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,47}$");
    public static bool IsTrackLayoutId(string? id)
    {
        const string prefix = "external-track-";
        if (id is null || id.Length > prefix.Length + 97 || !id.StartsWith(prefix, StringComparison.Ordinal) || id.Any(char.IsControl)) return false;
        var parts = id[prefix.Length..].Split('/');
        return parts.Length == 2 && Slug(parts[0]) && Slug(parts[1]);
    }
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
        if (m.Files is null || m.Files.Length is < 1 or > 4096 || m.Files.Any(f => f is null || !SafeFiles.Digest(f.Sha256) || (f.Bytes < 1 || f.Bytes > (m.Id == "pikes-peak-lab" ? 1610612736L : 805306368L))) ||
            m.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Files.Length || m.Files.Sum(f => f.Bytes) > (m.Id == "pikes-peak-lab" ? 12L : 4L) * 1024 * 1024 * 1024)
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
            if (m.Car.Audio is { } audio)
            {
                string code = m.Car.Code!; // a valid car code, checked above
                if (audio.Bank != "eng_" + code || audio.Samples is < 32000 or > 1048576) throw new IOException("Invalid external car audio.");
                roots = [.. roots, $"payload/{CarAudioFolder(code)}/", $"payload/{CarEngineIni(code)}"];
                foreach (string required in new[] { CarEngineIni(code), $"{CarAudioFolder(code)}/NESSamples.dic", $"{CarAudioFolder(code)}/{audio.Bank}.wip" })
                    if (!m.Files.Any(f => f.Path == "payload/" + required)) throw new IOException("Incomplete external car audio: " + required);
                if (m.Files.Any(f => f.Path.StartsWith("payload/audio/", StringComparison.Ordinal) && !CarAudioPath(f.Path["payload/".Length..]))) throw new IOException("Invalid external car audio file name.");
            }
            if (m.Car.Liveries is { } liveries)
            {
                if (liveries.Length is < 2 or > MaximumLiveries || liveries.Any(name => !Text(name, 32) || name != name.Trim()) ||
                    liveries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != liveries.Length) throw new IOException("Invalid external car livery names.");
                for (int livery = 0; livery < liveries.Length; livery++)
                    foreach (string required in LiveryFiles(m.Car.Code!, livery))
                        if (!m.Files.Any(f => f.Path == "payload/" + required)) throw new IOException("Incomplete external car livery: " + required);
            }
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
            if (layout.RaceStart is not ("grid" or "staggered"))
                throw new IOException("Unsupported external race start.");
            if (layout.RaceStart == "staggered")
            {
                if (layout.Circuit || layout.Discipline is not ("Rally" or "Trailblazer") || !m.Modes.Contains("desktop-race"))
                    throw new IOException("Staggered starts require a point-to-point Rally or Trailblazer race layout.");
                foreach (string required in new[] { "ai_track.xml", "dev_ai_track.xml", "ai_vehicle_track.xml" })
                    if (!used.Contains(prefix + "route_0/" + required)) throw new IOException("Staggered route is missing AI guidance: " + required);
                // Practice retains its original timing and placement data. Race uses
                // an explicitly authored start split and separated queue positions.
                var targets = new[] { "grids.pssg", "progress_track.xml", "ai_track.xml", "dev_ai_track.xml" }.Select(n => $"tracks/usa/{layout.Folder}/route_0/{n}").ToArray();
                if (layout.RaceSession is null || layout.RaceSession.Length != targets.Length ||
                    layout.RaceSession.Any(s => s is null) ||
                    !layout.RaceSession.Select(s => s.Target).Order().SequenceEqual(targets.Order()))
                    throw new IOException("Staggered routes need separate race placement, timing and queue guidance data.");
                foreach (var item in layout.RaceSession)
                {
                    if (!item.File.StartsWith($"payload/session/{layout.Id}/race/", StringComparison.Ordinal) ||
                        !m.Files.Any(f => f.Path == item.File && f.Bytes <= 64 * 1024 * 1024))
                        throw new IOException("Staggered race data is outside its own layout payload.");
                    used.Add(item.File);
                }
            }
            else if (layout.RaceSession is { Length: > 0 })
                throw new IOException("Separate race data requires a declared staggered start.");
            if (layout.GridMaximumOpponents != 0 || layout.GridRaceSession is not null)
            {
                if (layout.RaceStart != "staggered" || layout.GridMaximumOpponents is < 1 or > 7)
                    throw new IOException("An additional grid race requires a staggered layout and one to seven grid opponents.");
                var targets = new[] { "grids.pssg", "progress_track.xml", "ai_track.xml", "dev_ai_track.xml" }.Select(n => $"tracks/usa/{layout.Folder}/route_0/{n}").ToArray();
                if (layout.GridRaceSession is null || layout.GridRaceSession.Length != targets.Length ||
                    layout.GridRaceSession.Any(s => s is null) ||
                    !layout.GridRaceSession.Select(s => s.Target).Order().SequenceEqual(targets.Order()))
                    throw new IOException("Grid races need separate placement, timing and start guidance data.");
                foreach (var item in layout.GridRaceSession)
                {
                    if (!item.File.StartsWith($"payload/session/{layout.Id}/grid-race/", StringComparison.Ordinal) ||
                        !m.Files.Any(f => f.Path == item.File && f.Bytes <= 64 * 1024 * 1024))
                        throw new IOException("Grid race data is outside its own layout payload.");
                    used.Add(item.File);
                }
            }
            // A registered layout may ship no session replacements at all.
            if (layout.Session is null || layout.Session.Length is > 32 || (layout.Session.Length == 0 && layout.Registration is null) ||
                layout.Session.Any(s => s is null) || layout.Session.Select(s => s.Target).Distinct().Count() != layout.Session.Length)
                throw new IOException("Invalid external session replacements.");
            foreach (var replace in layout.Session)
            {
                if (!TrackPacks.SessionTargets.Contains(replace.Target) || !m.Dependencies.Any(d => d.Path == replace.Target) ||
                    !m.Files.Any(f => f.Path == replace.File && f.Bytes <= 64 * 1024 * 1024) || !replace.File.StartsWith("payload/session/", StringComparison.Ordinal))
                    throw new IOException("External session replacement is outside supported game targets.");
                used.Add(replace.File);
            }
            if (layout.VrSession is not null)
            {
                // Lighter versions of the layout's own packed files for VR sessions (for example sparser grass).
                if (layout.VrSession.Length is < 1 or > 8 || layout.VrSession.Any(s => s is null) ||
                    layout.VrSession.Select(s => s.Target).Distinct().Count() != layout.VrSession.Length)
                    throw new IOException("Invalid external VR replacements.");
                foreach (var item in layout.VrSession)
                {
                    if (!item.Target.StartsWith($"tracks/usa/{layout.Folder}/", StringComparison.Ordinal) || !m.Files.Any(f => f.Path == "payload/" + item.Target) ||
                        !item.File.StartsWith($"payload/session/{layout.Id}/vr/", StringComparison.Ordinal) ||
                        !m.Files.Any(f => f.Path == item.File && f.Bytes <= 64 * 1024 * 1024))
                        throw new IOException("External VR replacement is outside its own layout payload.");
                    used.Add(item.File);
                }
            }
            if (layout.Registration is not null)
            {
                // Registered in the player's own pinned files at launch, never also replaced by a shipped copy.
                TrackRegistration.Validate(layout.Registration);
                foreach (string target in TrackRegistration.Targets(layout.Registration))
                    if (layout.Session.Any(s => s.Target == target) || !m.Dependencies.Any(d => d.Path == target))
                        throw new IOException("External track registration needs the pinned original, and no shipped copy, of " + target);
            }
            else if (!layout.Session.Any(s => s.Target == "database/database.bin")) throw new IOException("External track needs its prepared database registration.");
            if (layout.Borrow is not null)
            {
                if (layout.Borrow.Length > 256 || layout.Borrow.Any(b => b is null) || layout.Borrow.Select(b => b.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != layout.Borrow.Length)
                    throw new IOException("Invalid external borrowed files.");
                foreach (var borrow in layout.Borrow)
                {
                    SafeFiles.Relative(borrow.Target); SafeFiles.Relative(borrow.Source);
                    // Into the layout's own new folder, from a pinned stock file; never over a packed file or out of another pack.
                    if (!borrow.Target.StartsWith(prefix["payload/".Length..], StringComparison.Ordinal) || !DataExtension(borrow.Target) ||
                        m.Files.Any(f => string.Equals(f.Path, "payload/" + borrow.Target, StringComparison.OrdinalIgnoreCase)) ||
                        !m.Dependencies.Any(d => d.Path == borrow.Source) || !DataExtension(borrow.Source) ||
                        borrow.Source.StartsWith("tracks/usa/d2ve_", StringComparison.OrdinalIgnoreCase) || TrackPacks.SessionTargets.Contains(borrow.Source))
                        throw new IOException("External borrowed file is outside its layout folder or not a pinned stock file: " + borrow.Target);
                }
            }
            if (layout.Splice is not null)
            {
                if (layout.Splice.Length > 16 || layout.Splice.Any(s => s is null)) throw new IOException("Invalid external spliced ranges.");
                foreach (var splice in layout.Splice)
                {
                    SafeFiles.Relative(splice.Target); SafeFiles.Relative(splice.Source);
                    var packed = m.Files.SingleOrDefault(f => f.Path == "payload/" + splice.Target);
                    // Into one of the layout's own packed files, from a pinned stock file, within the packed file's size.
                    if (packed is null || !splice.Target.StartsWith(prefix["payload/".Length..], StringComparison.Ordinal) ||
                        !m.Dependencies.Any(d => d.Path == splice.Source) || !DataExtension(splice.Source) ||
                        splice.Source.StartsWith("tracks/usa/d2ve_", StringComparison.OrdinalIgnoreCase) || TrackPacks.SessionTargets.Contains(splice.Source) ||
                        splice.Offset < 0 || splice.Length is < 1 or > 65536 || splice.Offset + splice.Length > packed.Bytes ||
                        layout.VrSession?.SingleOrDefault(s => s.Target == splice.Target) is { } vr && splice.Offset + splice.Length > m.Files.Single(f => f.Path == vr.File).Bytes)
                        throw new IOException("External spliced range is outside its layout's packed file or not from a pinned stock file: " + splice.Target);
                }
                if (layout.Splice.GroupBy(s => s.Target).Any(g => g.OrderBy(s => s.Offset).Zip(g.OrderBy(s => s.Offset).Skip(1)).Any(p => p.First.Offset + p.First.Length > p.Second.Offset)))
                    throw new IOException("External spliced ranges overlap.");
            }
        }
        if (used.Count != m.Files.Length) throw new IOException("External inventory contains unreferenced files or unsupported destinations.");
        var listed = new HashSet<string>(m.Layouts.Select(l => l.Id), StringComparer.Ordinal);
        foreach (var layout in m.Layouts.Where(l => l.Conditions is not null))
        {
            var conditions = layout.Conditions!;
            if (conditions.Length is < 2 or > 8 || conditions.Any(c => c is null || !Slug(c.Id) || !Text(c.Name, 40) || c.Name != c.Name.Trim()) ||
                conditions.Select(c => c.Id).Distinct().Count() != conditions.Length || conditions.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != conditions.Length)
                throw new IOException("Invalid external layout conditions.");
            string lighting = $"tracks/usa/{layout.Folder}/lighting.xml";
            foreach (var condition in conditions)
            {
                if (condition != conditions[0] && (!Slug(layout.Id + "-" + condition.Id) || !listed.Add(layout.Id + "-" + condition.Id)))
                    throw new IOException("An external condition's layout id is too long or already used: " + layout.Id + "-" + condition.Id);
                // Another pinned stock source for one of the layout's own borrowed targets.
                if (condition.Borrow is { } swaps && (swaps.Length > 16 || swaps.Any(b => b is null || layout.Borrow?.Any(l => l.Target == b.Target) != true ||
                        !m.Dependencies.Any(d => d.Path == b.Source) || !DataExtension(b.Source) || Path.GetExtension(b.Source) != Path.GetExtension(b.Target) ||
                        b.Source.StartsWith("tracks/usa/d2ve_", StringComparison.OrdinalIgnoreCase) || TrackPacks.SessionTargets.Contains(b.Source) ||
                        (b.Target == lighting && condition.Lighting is { Count: > 0 })) ||
                    swaps.Select(b => b.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != swaps.Length))
                    throw new IOException("Invalid external condition borrowed files: " + condition.Name);
                if (condition.Lighting is not { Count: > 0 } values) continue;
                // Values only for the borrowed stock lighting file, patched at launch; never a packed file.
                if (layout.Borrow?.Any(b => b.Target == lighting && b.Source.EndsWith("/lighting.xml", StringComparison.Ordinal)) != true || values.Count > 64 ||
                    values.Any(v => !Regex.IsMatch(v.Key, "^[A-Za-z_][A-Za-z0-9_]{0,63}$") || !ExternalLighting.Value(v.Value)))
                    throw new IOException("Invalid external condition lighting: " + condition.Name);
            }
        }
    }
}
