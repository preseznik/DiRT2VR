using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Nordschleife;

internal static class Files
{
    internal static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked paths are unsupported: " + p);
    }
    internal static string Inside(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(s => s is "" or "." or "..") || relative.Contains(':'))
            throw new IOException("Unsafe source path: " + relative);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        NoLinks(path);
        return path;
    }
    internal static void NewOutput(string output, params string[] sources)
    {
        output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output)); NoLinks(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new: " + output);
        foreach (var source in sources.Select(Path.GetFullPath))
            if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Output cannot be inside a source installation.");
        Directory.CreateDirectory(output);
    }
    internal static string Hash(string path)
    {
        NoLinks(path); using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream));
    }
    internal static PssgFile Pssg(string path)
    {
        NoLinks(path); using var input = File.OpenRead(path);
        // The upstream gzip reader uses a fixed temporary filename. Reject it here.
        if (input.ReadByte() == 0x1f) throw new IOException("Compressed PSSG needs a staging decode: " + path);
        input.Position = 0; return PssgFile.Open(input);
    }
    internal static XDocument Document(PssgFile file)
    {
        var root = new XElement("PSSGFILE"); file.RootElement.WriteXml(root); return new(root);
    }
    internal static PssgFile FromDocument(XDocument document)
    {
        using var memory = new MemoryStream(); document.Save(memory); memory.Position = 0;
        return PssgFile.ReadXml(memory, PssgFileType.Pssg);
    }
    internal static void Save(PssgFile file, string path)
    {
        using var output = new FileStream(path, FileMode.CreateNew); file.Save(output);
    }
    internal static void Xml(XDocument document, string path)
    {
        using var memory = new MemoryStream(); document.Save(memory); memory.Position = 0;
        using var output = new FileStream(path, FileMode.CreateNew); new XmlFile(memory).Write(output, XmlType.BinXml);
    }
    internal static void Json(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
