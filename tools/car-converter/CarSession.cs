using DiRT2VR.CustomTracks;

namespace DiRT2VR.CustomCars;

public static class CarSession
{
    public static void Prepare(string game, string track, Func<byte[], byte[]>? progress = null)
        => Plan(game, track, progress)();

    public static Action Plan(string game, string track, Func<byte[], byte[]>? progress = null)
    {
        CarPack.RequireMode(track, false, "practice", 0);
        SafeFiles.RequireClosed(); SessionFiles.Recover(game);
        var receipt = CarPack.Read(game);
        var trackPack = TrackPacks.IsLayout(track) ? TrackPacks.ForLayout(track) : null;
        var external = trackPack?.External;
        if (external is not null) { external.Verify(); external.RequireDependencies(game); }
        var files = new List<SessionReplacement>();
        foreach (var path in CarPack.SharedTargets)
        {
            string target = SafeFiles.Inside(game, path), hash = receipt.Sources.Single(f => f.Game == "dirt2" && f.Path == path).Sha256;
            byte[] original = File.ReadAllBytes(target);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)) != hash) throw new IOException("Custom car needs the original shared file: " + path + ". External edits were preserved.");
            var replacement = external?.Layout(track).Session.SingleOrDefault(s => s.Target == path);
            if (replacement is not null)
            {
                if (path != "database/database.bin") throw new IOException("This external track and Ferrari replace the same shared file: " + path);
                var file = external!.Manifest!.Files.Single(f => f.Path == replacement.File);
                original = File.ReadAllBytes(SafeFiles.Inside(external.Root, file.Path)); hash = file.Sha256;
            }
            files.Add(new(path, hash, path == "database/database.bin" ? original : CarRegistration.Shared(path, original)));
        }
        return () => SessionFiles.Prepare(game, trackPack is not null && external is null ? track : null, progress, files, CarRegistration.Database);
    }
}
