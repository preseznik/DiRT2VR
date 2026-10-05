namespace DiRT2VR.CustomTracks;

public sealed record TrackProgress(int Percent, string Message);
public sealed record Layout(string Id, string Name, string Folder, string Condition, string Discipline = "Rallycross");
public sealed record Fingerprint(string Game, string Path, string Sha256);
public sealed record PackFile(string Path, long Bytes, string Sha256);
public sealed record SessionFile(string Path, string OriginalSha256, string InstalledPath);
public sealed record LayoutSession(string LayoutId, SessionFile[] Files,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Version = null);
public sealed record PackReceipt(int Schema, string Id, string Version, string MinimumLauncher,
    PackFile[] Files, LayoutSession[] Sessions, Fingerprint[] Sources);
public sealed record ConversionProfile(string Id, string Name, string Version, string MinimumLauncher,
    long StagingBytes, long InstalledBytes,
    string[] Modes, Layout[] Layouts, Fingerprint[] Sources);

public static class AspenPack
{
    public const string Id = "aspen-rallycross";
    public const string Version = "1.1.1";
    public const string MinimumLauncher = "0.17.29";
    public const string Support = "DiRT2VR/custom-tracks/aspen-rallycross";
    public const string Receipt = Support + "/receipt.json";
    public static readonly string[] Modes = ["desktop-solo", "vr-solo", "desktop-race", "vr-race"];
    public static readonly Layout[] Layouts = [
        new("aspen-lakeside", "Lakeside", "d2vr_aspen", "Night"),
        new("aspen-lake-view", "Lake View", "d2vr_aspen_lv", "Morning sun"),
        new("aspen-snowmass-sprint", "Snowmass Sprint", "d2vr_aspen_ss", "Evening sun"),
        new("aspen-snowmass-loop", "Snowmass Loop", "d2vr_aspen_sl", "Overcast"),
        new("aspen-eagle-hill-rise", "Eagle Hill Rise", "d2vr_aspen_ehr", "Night", "Landrush"),
        new("aspen-eagle-hill-loop", "Eagle Hill Loop", "d2vr_aspen_ehl", "Night", "Landrush"),
        new("aspen-brush-creek-sprint", "Brush Creek Sprint", "d2vr_aspen_bcs", "Evening sun", "Landrush"),
        new("aspen-brush-creek-dash", "Brush Creek Dash", "d2vr_aspen_bcd", "Evening sun", "Landrush"),
        new("aspen-buttermilk-descent", "Buttermilk Descent", "d2vr_aspen_bd", "Overcast", "Head-to-head"),
        new("aspen-buttermilk-climb", "Buttermilk Climb", "d2vr_aspen_bc", "Overcast", "Head-to-head")];
    public static readonly string[] SharedTargets = ["surface_materials.xml", "database/database.bin",
        "language/language_eng.lng", "language/language_fre.lng", "language/language_ger.lng",
        "language/language_ita.lng", "language/language_jpn.lng", "language/language_pol.lng",
        "language/language_rus.lng", "language/language_spa.lng", "language/language_use.lng",
        "effects/pfx_kickup_data_set.xml", "effects/pfx_pssg_dataset.xml", "tracks/light_definitions.xml"];
    public static string[] InstallRoots => TrackPacks.Aspen.InstallRoots;
    public static Layout GetLayout(string id) => TrackPacks.Aspen.GetLayout(id);
    public static bool IsLayout(string id) => TrackPacks.Aspen.IsLayout(id);
    public static bool SupportsRace(PackReceipt receipt) => TrackPacks.Aspen.SupportsRace(receipt);
    public static void RequireMode(string id, bool vr, string mode, string car, int opponents, int laps) => TrackPacks.Aspen.RequireMode(id, vr, mode, car, opponents, laps);
    public static void Validate(ConversionProfile offer, string launcherVersion) => TrackPacks.Aspen.Validate(offer, launcherVersion);
    public static void Validate(PackReceipt receipt) => TrackPacks.Aspen.Validate(receipt);
    public static void ValidateSources(Fingerprint[] sources) => TrackPack.ValidateSources(sources);
    public static PackReceipt Read(string game, bool verify = true, CancellationToken cancel = default) => TrackPacks.Aspen.Read(game, verify, cancel);
    public static void Verify(string root, PackReceipt receipt, CancellationToken cancel = default) => TrackPacks.Aspen.Verify(root, receipt, cancel);
    public static void VerifySources(ConversionProfile offer, string dirt2, string dirt3, IProgress<TrackProgress>? progress, CancellationToken cancel) => TrackPack.VerifySources(offer, dirt2, dirt3, progress, cancel);
}

public static class TrackPacks
{
    public static readonly TrackPack Aspen = new(AspenPack.Id, "Aspen", AspenPack.Version, AspenPack.MinimumLauncher, AspenPack.Modes, AspenPack.Layouts,
        ["surface_materials.xml", "database/database.bin", "effects/pfx_kickup_data_set.xml", "effects/pfx_pssg_dataset.xml"]);
    public static readonly TrackPack Smelter = new("smelter", "Smelter", "1.1.4", "0.17.30", ["desktop-solo"],
        [new("smelter-county-loop", "County Loop", "d2vr_smelter_0", "Morning sun"),
         new("smelter-portage-canal", "Portage Canal", "d2vr_smelter_1", "Morning sun"),
         new("smelter-houghton-sprint", "Houghton Sprint", "d2vr_smelter_2", "Wet lighting (no rain)"),
         new("smelter-waterfront-park", "Waterfront Park", "d2vr_smelter_3", "Wet lighting (no rain)"),
         new("smelter-copper-run", "Copper Run", "d2vr_smelter_6", "Evening sun", "Landrush"),
         new("smelter-maple-woods", "Maple Woods", "d2vr_smelter_7", "Evening sun", "Landrush"),
         new("smelter-atlantic-mill", "Atlantic Mill", "d2vr_smelter_8", "Morning sun", "Landrush"),
         new("smelter-coles-creek", "Cole's Creek", "d2vr_smelter_9", "Morning sun", "Landrush"),
         new("smelter-dredger-duel", "Dredger Duel", "d2vr_smelter_4", "Evening sun", "Head-to-head"),
         new("smelter-furnace-duel", "Furnace Duel", "d2vr_smelter_5", "Evening sun", "Head-to-head")], ["surface_materials.xml", "database/database.bin"]);
    public static readonly TrackPack[] All = [Aspen, Smelter];
    public static readonly string[] SessionTargets = [..AspenPack.SharedTargets, "tracks/waterdefs.xml", "tracks/ornament_system_settings.xml"];
    public static TrackPack Get(string id) => All.SingleOrDefault(p => p.Id == id) ?? throw new IOException("Unknown custom-track pack.");
    public static bool IsLayout(string? id) => All.Any(p => p.Layouts.Any(l => l.Id == id));
    public static TrackPack ForLayout(string id) => All.SingleOrDefault(p => p.IsLayout(id)) ?? throw new IOException("Unknown custom layout.");
    public static TrackPack FromStaging(string directory)
    {
        var packs = All.Where(p => File.Exists(SafeFiles.Inside(directory, p.Receipt))).ToArray();
        if (packs.Length != 1) throw new IOException("Conversion staging must contain exactly one known pack.");
        return packs[0];
    }
}

// Destinations and supported modes are compiled in, never supplied by a receipt.
public sealed class TrackPack
{
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string MinimumLauncher { get; }
    public string[] Modes { get; }
    public Layout[] Layouts { get; }
    public string[] RequiredTargets { get; }
    public string Support => "DiRT2VR/custom-tracks/" + Id;
    public string Receipt => Support + "/receipt.json";
    public string[] SharedTargets => Id == "smelter" ? TrackPacks.SessionTargets : AspenPack.SharedTargets;
    internal TrackPack(string id, string name, string version, string minimumLauncher, string[] modes, Layout[] layouts, string[] requiredTargets)
    {
        Id = id; Name = name; Version = version; MinimumLauncher = minimumLauncher;
        Modes = modes; Layouts = layouts; RequiredTargets = requiredTargets;
    }
    public const string SelectiveBuildLauncher = "0.17.36";
    public string[] InstallRoots => Layouts.Select(l => "tracks/usa/" + l.Folder).Append(Support).ToArray();
    public Layout GetLayout(string id) => Layouts.SingleOrDefault(l => l.Id == id)
        ?? throw new IOException("This custom layout is unavailable. Select an installed layout.");
    public Layout[] SelectLayouts(IEnumerable<string>? ids)
    {
        if (ids is null) return Layouts.ToArray();
        var selected = ids.ToArray();
        if (selected.Length == 0 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length || selected.Any(id => !IsLayout(id)))
            throw new IOException("Select at least one available layout, without duplicates.");
        return Layouts.Where(l => selected.Contains(l.Id, StringComparer.Ordinal)).ToArray();
    }
    public bool Owns(Layout layout, string path) => path.StartsWith("tracks/usa/" + layout.Folder + "/", StringComparison.Ordinal) ||
        path.StartsWith(Support + "/" + layout.Id + "/", StringComparison.Ordinal);
    public Layout[] ReceiptLayouts(PackReceipt receipt) => receipt.Schema == 2
        ? SelectLayouts(receipt.Sessions?.Select(s => s?.LayoutId ?? "") ?? [])
        : System.Version.TryParse(receipt.Version, out var version) && version < new System.Version(1, 1, 0)
        ? Layouts.Take(Id == AspenPack.Id ? 4 : 1).ToArray() : Layouts;
    public string[] ReceiptRoots(PackReceipt receipt) => ReceiptLayouts(receipt).Select(l => "tracks/usa/" + l.Folder).Append(Support).ToArray();
    public bool DesktopPracticeOnly(string id) => !Modes.Contains("desktop-race") || GetLayout(id).Discipline != "Rallycross";
    public string PracticeCar(string id) => GetLayout(id).Discipline == "Landrush" ? "kin" : "sti";
    public bool IsLayout(string id) => Layouts.Any(l => l.Id == id);
    public bool SupportsRace(PackReceipt receipt, string? layoutId = null)
    {
        var sessions = receipt.Sessions.Where(s => layoutId is null || s.LayoutId == layoutId).ToArray();
        return Modes.Contains("desktop-race") && sessions.Length > 0 && sessions.All(s => System.Version.Parse(s.Version ?? receipt.Version) >= new System.Version(1, 0, 1));
    }
    public void RequireMode(string id, bool vr, string mode, string car, int opponents, int laps)
    {
        GetLayout(id);
        if (!Modes.Contains((vr ? "vr-" : "desktop-") + (mode == "race" ? "race" : "solo")) ||
            (DesktopPracticeOnly(id) && (vr || mode != "practice" || car != PracticeCar(id))))
            throw new IOException(GetLayout(id).Name + " currently supports desktop Direct practice only, using " +
                (PracticeCar(id) == "kin" ? "the Kincaid Ford F-150" : "the Subaru STI") + ". Race and VR are unavailable for this test layout.");
        if (mode is not ("practice" or "race") || string.IsNullOrWhiteSpace(car) || laps is < 1 or > 20 ||
            (mode == "practice" ? opponents != 0 : opponents is < 1 or > 7))
            throw new IOException("Choose Direct practice or Race, an installed car, and one to twenty laps. Race supports one to seven opponents. LAN is unavailable.");
    }
    public void Validate(ConversionProfile offer, string launcherVersion)
    {
        if (offer is null || offer.Id != Id || !SafeFiles.Version(offer.Version) || !SafeFiles.Version(offer.MinimumLauncher) ||
            System.Version.Parse(launcherVersion) < System.Version.Parse(offer.MinimumLauncher))
            throw new IOException("Update DiRT2VR before installing this custom-track package.");
        if (offer.StagingBytes is < 1024 * 1024 or > 16L * 1024 * 1024 * 1024 ||
            offer.InstalledBytes is <= 0 or > 4L * 1024 * 1024 * 1024 ||
            offer.Modes is null || !offer.Modes.SequenceEqual(Modes) ||
            offer.Layouts is null || !offer.Layouts.SequenceEqual(Layouts))
            throw new IOException("The bundled conversion tools are unsupported. Reinstall DiRT2VR.");
        ValidateSources(offer.Sources);
    }
    public static void ValidateSources(Fingerprint[] sources)
    {
        if (sources is null || sources.Length is < 2 or > 4096 || sources.Any(f => f is null) ||
            sources.Select(f => f.Game + "/" + f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Length ||
            sources.Any(f => f.Game is not ("dirt2" or "dirt3") || !SafeFiles.Digest(f.Sha256)))
            throw new IOException("Invalid source fingerprint inventory.");
        foreach (var f in sources) SafeFiles.Relative(f.Path);
        if (!sources.Any(f => f.Game == "dirt2") || !sources.Any(f => f.Game == "dirt3")) throw new IOException("Missing source inventory.");
    }
    public void Validate(PackReceipt receipt)
    {
        if (receipt is null || receipt.Schema is not (1 or 2) || receipt.Id != Id || !SafeFiles.Version(receipt.Version) || !SafeFiles.Version(receipt.MinimumLauncher) ||
            receipt.Files is null || receipt.Files.Length < (receipt.Schema == 1 && Id == AspenPack.Id ? 100 : 7) || receipt.Files.Length > 4096 || receipt.Files.Any(f => f is null) || receipt.Sessions is null || receipt.Sessions.Length != ReceiptLayouts(receipt).Length ||
            receipt.Sessions.Any(s => s is null || s.Files is null || s.Files.Any(f => f is null)) || receipt.Sessions.Select(s => s.LayoutId).Distinct().Count() != ReceiptLayouts(receipt).Length)
            throw new IOException("Unsupported custom-track receipt.");
        if (receipt.Schema == 2 && (System.Version.Parse(receipt.MinimumLauncher) < System.Version.Parse(SelectiveBuildLauncher) ||
            receipt.Sessions.Any(s => !SafeFiles.Version(s.Version!)))) throw new IOException("Invalid selective-build receipt.");
        if (receipt.Schema == 1 && receipt.Sessions.Any(s => s.Version is not null)) throw new IOException("Invalid legacy layout version.");
        ValidateSources(receipt.Sources);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedRoots = ReceiptLayouts(receipt).Select(l => "tracks/usa/" + l.Folder).Append(Support).ToArray();
        long total = 0;
        foreach (var f in receipt.Files)
        {
            SafeFiles.Relative(f.Path);
            if (!paths.Add(f.Path) || f.Path == Receipt || !allowedRoots.Any(p => f.Path.StartsWith(p + "/", StringComparison.Ordinal)) ||
                (receipt.Schema == 2 && !ReceiptLayouts(receipt).Any(l => Owns(l, f.Path))) ||
                !SafeFiles.Digest(f.Sha256) || f.Bytes is < 0 or > 512L * 1024 * 1024 ||
                !new[] { ".xml", ".bin", ".pssg", ".ens", ".jpk", ".vis", ".clm", ".grs", ".cqtc", ".cns", ".txt", ".lng", ".htf" }.Contains(Path.GetExtension(f.Path)))
                throw new IOException("Invalid custom-track file: " + f.Path);
            total = checked(total + f.Bytes);
        }
        if (total > 4L * 1024 * 1024 * 1024) throw new IOException("Custom-track inventory exceeds its size limit.");
        foreach (var layout in ReceiptLayouts(receipt))
        {
            foreach (var name in new[] { "track.jpk", "routesplit.pssg", "track.vis", "grids.pssg", "progress_track.xml" })
                if (!paths.Contains($"tracks/usa/{layout.Folder}/route_0/{name}")) throw new IOException("Incomplete custom layout: " + layout.Name);
            var session = receipt.Sessions.SingleOrDefault(s => s.LayoutId == layout.Id) ?? throw new IOException("Missing layout session inventory.");
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in session.Files)
            {
                if (!SharedTargets.Contains(f.Path) || !targets.Add(f.Path) || !SafeFiles.Digest(f.OriginalSha256) ||
                    f.InstalledPath is null || !f.InstalledPath.StartsWith(Support + "/" + layout.Id + "/", StringComparison.Ordinal) || !paths.Contains(f.InstalledPath))
                    throw new IOException("Invalid custom session file.");
                var source = receipt.Sources.SingleOrDefault(p => p.Game == "dirt2" && p.Path == f.Path);
                if (source?.Sha256 != f.OriginalSha256) throw new IOException("Session source fingerprint mismatch.");
            }
            // Older installed Smelter receipts remain readable for rebuild/uninstall.
            if (Id == "smelter" && System.Version.Parse(session.Version ?? receipt.Version) >= new System.Version(1, 0, 1) && !targets.Contains("tracks/waterdefs.xml"))
                throw new IOException("Missing Smelter water session definition. Rebuild from source.");
            if (Id == "smelter" && System.Version.Parse(session.Version ?? receipt.Version) >= new System.Version(1, 1, 1) && !targets.Contains("tracks/ornament_system_settings.xml"))
                throw new IOException("Missing Smelter scenery session definition. Rebuild from source.");
            foreach (var required in RequiredTargets)
                if (!targets.Contains(required)) throw new IOException("Incomplete session inventory.");
        }
    }
    public PackReceipt Read(string game, bool verify = true, CancellationToken cancel = default)
    {
        var receipt = SafeFiles.ReadJson<PackReceipt>(SafeFiles.Inside(game, Receipt));
        Validate(receipt);
        if (verify) Verify(game, receipt, cancel);
        return receipt;
    }
    public void Verify(string root, PackReceipt receipt, CancellationToken cancel = default)
    {
        Validate(receipt);
        foreach (var file in receipt.Files)
        {
            cancel.ThrowIfCancellationRequested();
            var path = SafeFiles.Inside(root, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes || SafeFiles.Hash(path) != file.Sha256)
                throw new IOException("Custom-track file is missing or changed: " + file.Path + ". Use Manage > Rebuild from source.");
        }
    }
    public static void VerifySources(ConversionProfile offer, string dirt2, string dirt3, IProgress<TrackProgress>? progress, CancellationToken cancel)
    {
        ValidateSources(offer.Sources);
        for (int i = 0; i < offer.Sources.Length; i++)
        {
            cancel.ThrowIfCancellationRequested(); var source = offer.Sources[i];
            string path = SafeFiles.Inside(source.Game == "dirt2" ? dirt2 : dirt3, source.Path);
            if (!File.Exists(path) || SafeFiles.Hash(path) != source.Sha256)
                throw new IOException($"Missing or unsupported {source.Game} source file: {source.Path}. Choose the correct game folder or verify the original files in Steam.");
            progress?.Report(new((i + 1) * 100 / offer.Sources.Length, "Checking original game files"));
        }
    }
}
