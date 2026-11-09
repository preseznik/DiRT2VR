using DiRT2VR.CustomCars;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Multiplayer;

internal sealed record DirectCarFiles(string Code, string Donor, string SourceRoot, PackFile[] Files);

// Custom vehicle database IDs are assigned during registration. Use the pack
// code and fingerprint to identify the same selection on both PCs.
public sealed record DirectCustomCar(string Code, string Digest)
{
    internal bool Valid => Code is { Length: 3 } && (Code == CarPack.Code || ExternalPacks.AvailableCarCode(Code) && !DirectStockCatalog.StockCarCodes.Contains(Code))
        && SafeFiles.Digest(Digest);

    static IEnumerable<ExternalPack> ExternalCars(string game) => TrackPacks.External.Where(pack =>
        pack.Ready && pack.Kind == "car" && !DirectStockCatalog.StockCarCodes.Contains(pack.Manifest!.Car!.Code) &&
        pack.BelongsToInstallation(game));

    internal bool Matches(string install, string stockRoot, CancellationToken cancel)
        => Resolve(install, stockRoot, cancel) != null;

    internal string? Discipline()
    {
        string? donor = Code == CarPack.Code ? "prc" : TrackPacks.External.FirstOrDefault(p => p.Ready &&
            p.Kind == "car" && p.ManifestHash == Digest && p.Manifest?.Car?.Code == Code)?.Manifest?.Car?.Donor;
        return donor == null ? null : DirectStockCatalog.CarDisciplines.GetValueOrDefault(donor);
    }

    internal DirectCarFiles? Resolve(string install, string stockRoot, CancellationToken cancel, bool? vr = null)
    {
        if (!Valid) return null;
        if (Code == CarPack.Code)
        {
            var receipt = CarPack.Read(install, verify: false);
            if (CarPack.ContentId(receipt) != Digest) return null;
            foreach (string path in CarPack.SharedTargets)
            {
                var required = receipt.Sources.Single(s => s.Game == "dirt2" && s.Path == path);
                string stockFile = SafeFiles.Inside(stockRoot, path);
                if (!File.Exists(stockFile) || SafeFiles.Hash(stockFile, cancel) != required.Sha256) return null;
            }
            CarPack.Verify(install, receipt, cancel);
            // Conversion reports stay in the mod installation. The private game only needs runtime data.
            var files = receipt.Files.Where(f => !f.Path.StartsWith(CarPack.Support + "/", StringComparison.Ordinal)).ToArray();
            if (files.Any(f => !PrivateCars.FerrariAsset(f.Path))) throw new IOException("Unsupported file in the Ferrari runtime assets.");
            return new(Code, "prc", install, files);
        }
        var pack = ExternalCars(install).FirstOrDefault(p => p.Manifest!.Car!.Code == Code && p.ManifestHash == Digest);
        if (pack == null) return null;
        if (vr.HasValue) DiRT2VR.CustomCars.ExternalCars.RequireMode(pack, vr.Value, "race");
        pack.Verify(cancel);
        pack.RequireDependencies(stockRoot, cancel);
        return new(Code, pack.Manifest!.Car!.Donor, SafeFiles.Inside(pack.Root, "payload"),
            pack.Manifest.Files.Select(f => new PackFile(f.Path["payload/".Length..], f.Bytes, f.Sha256)).ToArray());
    }

    public static IEnumerable<RallycrossChoice> Installed(string game)
    {
        DirectCustomCar? ferrari = null;
        try
        {
            if (File.Exists(SafeFiles.Inside(game, CarPack.Receipt)))
                ferrari = new(CarPack.Code, CarPack.ContentId(CarPack.Read(game, verify: false)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        if (ferrari != null) yield return new(0, CarPack.Name + " (custom)", CustomCar: ferrari);

        foreach (var pack in ExternalCars(game))
            yield return new(0, pack.Name + " (external)", CustomCar: new(pack.Manifest!.Car!.Code, pack.ManifestHash));
    }
}
