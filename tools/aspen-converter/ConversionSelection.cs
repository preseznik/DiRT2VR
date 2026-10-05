using DiRT2VR.CustomTracks;
namespace DiRT2VR.Aspen;
internal static class ConversionSelection
{
    internal static string[]? Parse(string[] args, string command, TrackPack pack)
    {
        if (args.Length is not (4 or 6) || args[0] != command || args.Skip(1).Any(string.IsNullOrWhiteSpace) ||
            (args.Length == 6 && args[4] != "--layouts"))
            throw new IOException($"Usage: DiRT2VR.exe {command} <DiRT 3 folder> <DiRT 2 folder> <new output folder> [--layouts <comma-separated layout IDs>]");
        var ids = args.Length == 6 ? args[5].Split(',') : null;
        pack.SelectLayouts(ids);
        return ids;
    }
}
