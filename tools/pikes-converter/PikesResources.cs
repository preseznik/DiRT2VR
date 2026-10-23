using System.Reflection;
internal static class PikesResources
{
    internal static string Text(string name)
    {
        using var stream=typeof(PikesResources).Assembly.GetManifestResourceStream("Pikes."+name)
            ?? throw new InvalidDataException("Missing Pikes converter resource: "+name);
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
}
