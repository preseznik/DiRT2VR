using DiRT2VR.CustomTracks;

namespace DiRT2VR.Multiplayer;

// The renderer is local to one player. Its ownership record survives interrupted races
// and package updates, so a subsequent desktop start always gets a clean game folder.
internal static class PrivateVrRenderer
{
    const string Receipt = "vr-renderer.json";
    const string Proxy = "game/d3d11.dll";

    internal static void Prepare(string cache, string packageRoot)
    {
        string source = SafeFiles.Inside(packageRoot, "DiRT2VR/payload/d3d11.dll");
        string expected = DirectRace.PayloadHash(packageRoot, "d3d11.dll");
        string target = SafeFiles.Inside(cache, Proxy);
        if (Path.Exists(target)) throw new IOException("An existing d3d11.dll occupies the private Direct folder.");
        var temporary = SafeFiles.Inside(cache, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, temporary);
            if (PrivateGame.Hash(temporary) != expected) throw new IOException("The packaged VR renderer is damaged.");
            SafeFiles.WriteJson(SafeFiles.Inside(cache, Receipt), expected);
            File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) SafeFiles.DeleteOwned(temporary); }
    }

    internal static void Recover(string cache)
    {
        string receipt = SafeFiles.Inside(cache, Receipt);
        if (!File.Exists(receipt)) return;
        SafeFiles.RequireClosed();
        string expected = SafeFiles.ReadJson<string>(receipt);
        if (!SafeFiles.Digest(expected)) throw new IOException("Invalid private VR renderer recovery record.");
        string target = SafeFiles.Inside(cache, Proxy);
        if (Path.Exists(target))
        {
            if (!File.Exists(target) || PrivateGame.Hash(target) != expected)
                throw new IOException("The private VR renderer changed. Its file and recovery record were preserved.");
            SafeFiles.DeleteOwned(target);
        }
        File.Delete(receipt);
    }
}
