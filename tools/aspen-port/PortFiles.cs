using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Xml;

internal static class PortFiles
{
    internal static void NoLinks(string path)
    {
        for (var cursor = Path.GetFullPath(path); !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked path is not supported: " + cursor);
    }
    internal static FileStream OpenRead(string path) { NoLinks(path); return File.OpenRead(path); }
    internal static void NewOutput(string output, params string[] sources)
    {
        output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        NoLinks(output);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output must be new: " + output);
        foreach (var source in sources.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))))
            if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Output must be outside source installations.");
        Directory.CreateDirectory(output);
    }
    internal static XDocument ReadPssg(string path)
    {
        using var stream = OpenRead(path);
        int first = stream.ReadByte(); stream.Position = 0;
        if (first == '<')
        {
            Entities.ValidateText(XDocument.Load(stream));
            stream.Position = 0;
        }
        var root = new XElement("PSSGFILE", new XAttribute("version", "1.0.0.0"));
        PssgFile.Open(stream).RootElement.WriteXml(root);
        return new XDocument(root);
    }
    internal static void WritePssg(XDocument doc, string path)
    {
        using var memory = new MemoryStream(); doc.Save(memory); memory.Position = 0;
        var file = PssgFile.ReadXml(memory, PssgFileType.Pssg);
        using var stream = new FileStream(path, FileMode.CreateNew); file.Save(stream);
    }
    internal static XDocument ReadXml(string path)
    {
        using var stream = OpenRead(path);
        return XDocument.Parse(new XmlFile(stream).Document.OuterXml);
    }
    internal static void WriteXml(XDocument doc, string path, XmlType type = XmlType.BxmlBig)
    {
        using var memory = new MemoryStream(); doc.Save(memory); memory.Position = 0;
        using var stream = new FileStream(path, FileMode.CreateNew);
        // DiRT 2's material loader expects big-endian BXML, not the later BinXml format.
        new XmlFile(memory).Write(stream, type);
    }
    internal static IEnumerable<string> AspenPssg(string source) => Directory.EnumerateFiles(source).Concat(Directory.EnumerateFiles(Path.Combine(source, "route_0")))
        .Where(p => Path.GetExtension(p) is ".pssg" or ".ens").Order(StringComparer.Ordinal);
    internal static string Hash(string path) { using var stream = OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    internal static void Json(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
