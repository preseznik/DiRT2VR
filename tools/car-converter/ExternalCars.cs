using DiRT2VR.CustomTracks;

namespace DiRT2VR.CustomCars;

// One registry is shared by the selectors, launch preflight and replay checks.
public static class ExternalCars
{
    public static ExternalPack? Find(string code) => TrackPacks.External.SingleOrDefault(p =>
        p.Kind == "car" && p.Ready && p.Manifest!.Car!.Code == code);
    public static ExternalPack Require(string code) => Find(code) ?? throw new IOException("The external car is missing or incompatible. Restore its complete pack, then Refresh.");
    public static void RequireMode(ExternalPack pack, bool vr, string mode, bool replay = false)
    {
        if (!pack.Ready) throw new IOException(pack.Problem);
        string requested = (vr ? "vr-" : "desktop-") + (mode == "practice" ? "solo" : mode);
        if (mode is not ("practice" or "race") || !pack.Manifest!.Modes.Contains(requested))
            throw new IOException(pack.Name + " does not support this launch mode. Choose a mode listed by the pack.");
        if (replay && !pack.Manifest.Replay) throw new IOException(pack.Name + " does not support full replay recording or playback.");
    }

    // Compute and validate registration before installing any payload. For an external
    // track the base database is its already-validated replacement, not the stock one.
    public static Action Plan(string game, ExternalPack pack, string track, Func<byte[], byte[]>? progress = null)
    {
        pack.Verify(); pack.RequireDependencies(game);
        var car = pack.Manifest!.Car!;
        var trackPack = TrackPacks.IsLayout(track) ? TrackPacks.ForLayout(track) : null;
        var externalTrack = trackPack?.External;
        var files = new List<SessionReplacement>();
        byte[]? databaseInput = null;
        foreach (string path in CarPack.SharedTargets)
        {
            byte[] original = File.ReadAllBytes(SafeFiles.Inside(game, path));
            string hash = pack.Manifest.Dependencies.Single(d => d.Path == path).Sha256;
            var trackReplacement = externalTrack?.Layout(track).Session.SingleOrDefault(s => s.Target == path);
            if (trackReplacement is not null)
            {
                if (path != "database/database.bin") throw new IOException("This external track and car replace the same shared file: " + path);
                var file = externalTrack!.Manifest!.Files.Single(f => f.Path == trackReplacement.File);
                original = File.ReadAllBytes(SafeFiles.Inside(externalTrack.Root, file.Path)); hash = file.Sha256;
            }
            if (path == "database/database.bin") databaseInput = original;
            files.Add(new(path, hash, path == "database/database.bin" ? original : CarRegistration.Shared(path, original, car.Code, car.Donor)));
        }
        if (trackPack is not null && externalTrack is null)
        {
            var receipt = trackPack.Read(game);
            var session = receipt.Sessions.Single(s => s.LayoutId == trackPack.GetLayout(track).Id);
            var entry = session.Files.Single(s => s.Path == "database/database.bin");
            if (entry.OriginalSha256 != files.Single(f => f.Path == entry.Path).OriginalSha256)
                throw new IOException("Track and car need different game databases.");
            databaseInput = File.ReadAllBytes(SafeFiles.Inside(game, entry.InstalledPath));
        }
        byte[] registered = CarRegistration.Database(databaseInput!, car.Code, car.Donor);
        string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(databaseInput!));
        return () => SessionFiles.Prepare(game, externalTrack is null && trackPack is not null ? track : null,
            progress, files, source => {
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)) != expected)
                    throw new IOException("Track database changed after external-car preflight.");
                return registered;
            });
    }
}
