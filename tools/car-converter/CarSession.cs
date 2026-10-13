using DiRT2VR.CustomTracks;

namespace DiRT2VR.CustomCars;

public static class CarSession
{
    public static void Prepare(string game, string track, Func<byte[], byte[]>? progress = null)
    {
        CarPack.RequireMode(track, false, "practice", 0);
        SafeFiles.RequireClosed(); SessionFiles.Recover(game);
        var receipt = CarPack.Read(game);
        var files = new List<SessionReplacement>();
        foreach (var path in CarPack.SharedTargets)
        {
            string target = SafeFiles.Inside(game, path), hash = receipt.Sources.Single(f => f.Game == "dirt2" && f.Path == path).Sha256;
            byte[] original = File.ReadAllBytes(target);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)) != hash) throw new IOException("Custom car needs the original shared file: " + path + ". External edits were preserved.");
            files.Add(new(path, hash, path == "database/database.bin" ? original : CarRegistration.Shared(path, original)));
        }
        SessionFiles.Prepare(game, TrackPacks.IsLayout(track) ? track : null, progress, files, CarRegistration.Database);
    }
}
