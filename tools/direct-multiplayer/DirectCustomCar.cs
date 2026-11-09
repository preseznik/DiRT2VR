using DiRT2VR.CustomCars;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Multiplayer;

// Custom vehicle database IDs are assigned during registration. Use the pack
// code and fingerprint to identify the same selection on both PCs.
public sealed record DirectCustomCar(string Code, string Digest)
{
    internal bool Valid => Code is { Length: 3 } && (Code == CarPack.Code || ExternalPacks.AvailableCarCode(Code) && !DirectStockCatalog.StockCarCodes.Contains(Code))
        && SafeFiles.Digest(Digest);

    static IEnumerable<ExternalPack> ExternalCars(string game) => TrackPacks.External.Where(pack =>
        pack.Ready && pack.Kind == "car" && !DirectStockCatalog.StockCarCodes.Contains(pack.Manifest!.Car!.Code) &&
        StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(pack.Root), SafeFiles.Inside(game, pack.Support)));

    internal bool Matches(string game, CancellationToken cancel)
    {
        if (!Valid) return false;
        if (Code == CarPack.Code)
        {
            var receipt = CarPack.Read(game, verify: false);
            if (CarPack.ContentId(receipt) != Digest) return false;
            CarPack.Verify(game, receipt, cancel);
            return true;
        }
        var pack = ExternalCars(game).FirstOrDefault(p => p.Manifest!.Car!.Code == Code && p.ManifestHash == Digest);
        if (pack == null) return false;
        pack.Verify(cancel);
        pack.RequireDependencies(game, cancel);
        return true;
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
