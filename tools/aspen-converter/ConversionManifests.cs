// Conversion evidence only. Shared-file mutation lives in the launcher's
// fixed-purpose file worker; the downloaded converter never changes a game.
internal static class MetadataTransaction
{
    internal sealed record Entry(string Path, string OriginalHash, string AppliedHash);
    internal sealed record Manifest(int Schema, Entry[] Files);
}
internal static class EffectsTransaction
{
    internal static readonly string[] Targets = ["effects/pfx_kickup_data_set.xml", "effects/pfx_pssg_dataset.xml"];
    internal sealed record Entry(string Path, string OriginalHash, string AppliedHash);
    internal sealed record Manifest(int Schema, Entry[] Files);
}
