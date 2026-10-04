internal static class AspenMetadata
{
    internal static void Create(string game, string schema, string output, Dictionary<string,string>? inputs = null, AspenLayout? layout = null, float length = 2574.991f, AspenCondition? condition = null)
    {
        layout ??= AspenLayout.Lakeside;
        TrackMetadata.Create(game, schema, output,
            new(layout.DirectoryName, layout.StringId, layout.DisplayName, "aspen", "ASPEN", layout.DonorModel), length, condition?.Night, inputs);
    }
}
