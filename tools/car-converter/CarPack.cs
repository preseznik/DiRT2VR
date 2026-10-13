using DiRT2VR.CustomTracks;
using System.Text.Json;

namespace DiRT2VR.CustomCars;

public sealed record CarReceipt(int Schema, string Code, string Version, PackFile[] Files, Fingerprint[] Sources);

public static class CarPack
{
    public const string Code = "f04";
    public const string Name = "Ferrari F2004";
    public const string Version = "1.0.0";
    public const string Support = "DiRT2VR/custom-cars/f04";
    // Native audio lookup truncates references beyond 31 characters in the inspected game.
    public const string EngineIni = "audio/cars/f04engine.ini";
    public const string Receipt = Support + "/receipt.json";
    public static readonly string[] SharedTargets = ["database/database.bin", "cars/class_mappings.xml", "audio/carsetup.bin", "audio/carsetup_ai.bin", "audio/dictionaries.bin"];
    public static readonly string[] Roots = ["cars/f04", "interiors/f04", "audio/dictionaries/d2vr_f04", Support];

    public static Fingerprint[] Sources()
    {
        using var stream = typeof(CarPack).Assembly.GetManifestResourceStream("f2004-sources.json")!;
        return JsonSerializer.Deserialize<Fingerprint[]>(stream)!;
    }
    public static bool IsCustom(string? code) => code == Code;
    public static bool Owns(string path) => path == EngineIni || Roots.Any(root => path.StartsWith(root + "/", StringComparison.Ordinal));
    public static void RequireMode(string track, bool vr, string mode, int opponents)
    {
        if (vr) throw new IOException("Ferrari F2004 VR is awaiting headset validation. Use Launch for desktop play.");
        if (mode != "practice" || opponents != 0) throw new IOException("Ferrari F2004 currently supports Direct practice only. AI, LAN and career are unavailable.");
        if (track is not ("144" or "nordschleife-daylight")) throw new IOException("Ferrari F2004 currently supports Battersea Air and Nordschleife Daylight. Further track combinations await testing.");
    }
    public static CarReceipt Read(string game, bool verify = true)
    {
        var receipt = SafeFiles.ReadJson<CarReceipt>(SafeFiles.Inside(game, Receipt));
        Validate(receipt);
        if (verify) Verify(game, receipt);
        return receipt;
    }
    public static void Validate(CarReceipt receipt)
    {
        if (receipt is null || receipt.Schema != 1 || receipt.Code != Code || receipt.Version != Version ||
            receipt.Files is null || receipt.Files.Length is < 8 or > 256 || receipt.Files.Any(f => f is null || f.Bytes <= 0 || f.Bytes > 256L * 1024 * 1024 || !SafeFiles.Digest(f.Sha256) || !Owns(f.Path) || f.Path == Receipt) ||
            receipt.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.Files.Length ||
            receipt.Sources is null || !receipt.Sources.SequenceEqual(Sources())) throw new IOException("Unsupported custom-car installation receipt.");
        foreach (var file in receipt.Files) SafeFiles.Relative(file.Path);
        foreach (string required in new[] { "cars/f04/cameras.xml", "cars/f04/f04_highLOD.pssg", "cars/f04/f04_lowLOD.pssg", "cars/f04/f04.nd2", "cars/f04/f04.ctf", "interiors/f04/int_f04.pssg", EngineIni, "audio/dictionaries/d2vr_f04/NESSamples.dic" })
            if (!receipt.Files.Any(f => f.Path == required)) throw new IOException("Incomplete custom-car receipt: " + required);
    }
    public static void Verify(string game, CarReceipt receipt, CancellationToken cancel = default)
    {
        Validate(receipt);
        foreach (var file in receipt.Files)
        {
            string path = SafeFiles.Inside(game, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes || SafeFiles.Hash(path, cancel) != file.Sha256)
                throw new IOException("Custom-car file is missing or changed: " + file.Path + ". Rebuild the car from source.");
        }
    }
}
