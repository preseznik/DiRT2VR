using System.Security.Cryptography;
using DiRT2VR.CustomCars;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Multiplayer;

internal static class PrivateCars
{
    const string JournalFolder = "car-session";

    internal static bool FerrariAsset(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (path.StartsWith("cars/f04/", StringComparison.Ordinal) || path.StartsWith("interiors/f04/", StringComparison.Ordinal))
            return extension is ".pssg" or ".ctf" or ".nd2" or ".xml" or ".ppts";
        return path == CarPack.EngineIni || path.StartsWith("audio/dictionaries/d2vr_f04/", StringComparison.Ordinal)
            && extension is ".dic" or ".wip";
    }

    static void Validate(ExternalSession.Journal journal) =>
        ExternalSession.Validate(journal, 2 * 4096 + CarPack.SharedTargets.Length,
            path => CarPack.SharedTargets.Contains(path),
            path => FerrariAsset(path) || ExternalPacks.CarAssetPath(path) && !DirectStockCatalog.StockCarCodes.Contains(path.Split('/')[1]));

    internal static void Recover(string cache) =>
        ExternalSession.Recover(SafeFiles.Inside(cache, "game"), SafeFiles.Inside(cache, JournalFolder), Validate);

    // The caller owns the session mutex and has prepared the stock game and any selected track.
    internal static void Activate(string cache, string install, string stockRoot, Grid grid, bool vr, CancellationToken cancel)
    {
        SafeFiles.RequireClosed();
        if (grid.Guest == null || grid.HostCarsMatch != true || grid.GuestCarsMatch != true)
            throw new IOException("Both PCs must have matching custom cars before preparation.");
        var choices = new[] { grid.Host.CustomCar, grid.Guest.CustomCar }.OfType<DirectCustomCar>()
            .Distinct().OrderBy(c => c.Code, StringComparer.Ordinal).ToArray();
        if (choices.Length == 0 || choices.Select(c => c.Code).Distinct().Count() != choices.Length)
            throw new IOException("Each custom car code must identify one matching pack.");
        var cars = choices.Select(c => c.Resolve(install, stockRoot, cancel, vr)
            ?? throw new IOException("The selected custom car or its stock files changed: " + c.Code)).ToArray();
        string game = SafeFiles.Inside(cache, "game");
        string folder = SafeFiles.Inside(cache, JournalFolder);
        if (File.Exists(SafeFiles.Inside(folder, "pending.json"))) throw new IOException("Private car recovery is pending.");
        string temporary = "car-registration-" + Guid.NewGuid().ToString("N");
        string work = SafeFiles.Inside(cache, temporary);
        try
        {
            var writes = new List<(ExternalSession.Entry Entry, string Source)>();
            foreach (var car in cars)
            {
                ExternalSession.RequireEmptyAssetFolder(game, "cars/" + car.Code);
                ExternalSession.RequireEmptyAssetFolder(game, "interiors/" + car.Code);
                if (car.Code == CarPack.Code)
                    ExternalSession.RequireEmptyAssetFolder(game, "audio/dictionaries/d2vr_f04");
                foreach (var file in car.Files)
                    writes.Add((new(file.Path, null, file.Sha256, null), SafeFiles.Inside(car.SourceRoot, file.Path)));
            }

            // Use the track's prepared database as input. Sorting by code gives both PCs
            // the same generated vehicle and livery IDs, whichever seat chose each car.
            foreach (string path in CarPack.SharedTargets)
            {
                cancel.ThrowIfCancellationRequested();
                string target = SafeFiles.Inside(game, path);
                byte[] bytes = File.ReadAllBytes(target);
                string original = Convert.ToHexString(SHA256.HashData(bytes));
                foreach (var car in cars)
                    bytes = CarRegistration.Shared(path, bytes, car.Code, car.Donor, car.Code == CarPack.Code);
                string applied = Convert.ToHexString(SHA256.HashData(bytes));
                if (applied == original) continue;
                string staged = SafeFiles.Inside(work, path);
                SafeFiles.Atomic(staged, bytes);
                writes.Add((new(path, original, applied, File.GetAttributes(target)), staged));
            }
            ExternalSession.Apply(game, folder, writes, Validate, null, cancel);
        }
        catch
        {
            Recover(cache);
            throw;
        }
        finally { SafeFiles.DeleteWorkTree(cache, temporary); }
    }
}
