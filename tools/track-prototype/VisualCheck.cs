using System.Text.Json;
using System.Xml.Linq;
using EgoEngineLibrary.Graphics.Pssg;

internal static class VisualCheck
{
    internal static string[] Errors(PssgFile file)
    {
        using var stream = new MemoryStream();
        file.WriteXml(stream);
        stream.Position = 0;
        var document = XDocument.Load(stream);
        var errors = new List<string>();
        var objects = document.Descendants().Where(e => e.Attribute("id") is not null).ToArray();
        foreach (var group in objects.GroupBy(e => (string)e.Attribute("id")!))
            if (group.Count() > 1)
                errors.Add($"Duplicate object ID {group.Key}: {string.Join(", ", group.Select(e => e.Name.LocalName))}");

        var sources = document.Descendants("RENDERDATASOURCE").ToArray();
        foreach (var source in sources)
        {
            var name = (string?)source.Attribute("id");
            if ((string?)source.Attribute("primitive") != "triangles")
                errors.Add($"Render data {name} must declare primitive=triangles for DiRT 2 terrain.");
            var indices = source.Element("RENDERINDEXSOURCE");
            if (indices is null || (string?)indices.Attribute("primitive") != "triangles" ||
                (int?)indices.Attribute("count") is not int count || count <= 0 || count % 3 != 0)
                errors.Add($"Render data {name} has no valid triangle index source.");
            if ((int?)source.Attribute("streamCount") != source.Elements("RENDERSTREAM").Count())
                errors.Add($"Render data {name} stream count differs from its streams.");
        }
        foreach (var instance in document.Descendants("RENDERSTREAMINSTANCE"))
        {
            var reference = (string?)instance.Attribute("indices");
            if (sources.Count(s => "#" + (string?)s.Attribute("id") == reference) != 1 ||
                (string?)instance.Element("RENDERINSTANCESOURCE")?.Attribute("source") != reference)
                errors.Add($"Render instance {(string?)instance.Attribute("id")} has an unresolved/ambiguous source.");
        }
        return errors.ToArray();
    }

    internal static void Validate(PssgFile file)
    {
        var errors = Errors(file);
        if (errors.Length > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    internal static int Run(string input, string report)
    {
        using var stream = File.OpenRead(input);
        var errors = Errors(PssgFile.Open(stream));
        using var output = new FileStream(report, FileMode.CreateNew);
        JsonSerializer.Serialize(output, new { Input = Path.GetFullPath(input), Passed = errors.Length == 0, Errors = errors }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(errors.Length == 0 ? "Visual structure checks passed." : string.Join(Environment.NewLine, errors));
        return errors.Length == 0 ? 0 : 1;
    }
}
