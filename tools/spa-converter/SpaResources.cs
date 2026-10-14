internal static class SpaResources
{
    internal static string Text(string name)
    {
        using var stream = typeof(SpaResources).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException("Missing Spa converter resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
