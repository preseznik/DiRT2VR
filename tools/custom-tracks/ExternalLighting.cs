using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.CustomTracks;

// An external layout's condition (ExternalCondition.Lighting): parameters of the player's own stock lighting.xml,
// borrowed into the layout's folder, set for the session. The file keeps its format; everything else is unchanged.
public static class ExternalLighting
{
    // One to four plain decimal numbers, e.g. "12" or "1.05 0.68 0.42".
    internal static bool Value(string? value) => value is not null && value.Length <= 64 &&
        Regex.IsMatch(value, @"^-?\d{1,6}(\.\d{1,6})?( -?\d{1,6}(\.\d{1,6})?){0,3}$");

    public static byte[] Patch(byte[] original, IReadOnlyDictionary<string, string> values)
    {
        XmlFile source; using (var input = new MemoryStream(original, false)) source = new XmlFile(input);
        var document = XDocument.Parse(source.Document.OuterXml);
        foreach (var (name, value) in values)
        {
            if (!Value(value)) throw new IOException("Invalid track lighting value: " + name);
            var parameters = document.Descendants("param").Where(p => p.Attribute(name) is not null).ToArray();
            if (parameters.Length != 1) throw new IOException("The track lighting has no single parameter " + name + ".");
            parameters[0].SetAttributeValue(name, value);
        }
        using var memory = new MemoryStream(); document.Save(memory); memory.Position = 0;
        using var output = new MemoryStream(); new XmlFile(memory).Write(output, source.Type);
        byte[] bytes = output.ToArray();
        // Read back: every value as set, in the original format.
        XmlFile check; using (var readback = new MemoryStream(bytes, false)) check = new XmlFile(readback);
        var saved = XDocument.Parse(check.Document.OuterXml);
        foreach (var (name, value) in values)
        {
            string? stored = saved.Descendants("param").Select(p => (string?)p.Attribute(name)).SingleOrDefault(v => v is not null);
            if (stored is null || !Same(stored, value)) throw new IOException("Track lighting readback failed: " + name);
        }
        if (check.Type != source.Type) throw new IOException("Track lighting format changed.");
        return bytes;
    }

    // Binary XML may store numbers with its own formatting; compare them as numbers.
    static bool Same(string stored, string value)
    {
        var a = stored.Split(' ', StringSplitOptions.RemoveEmptyEntries); var b = value.Split(' ');
        return a.Length == b.Length && a.Zip(b).All(p => float.TryParse(p.First, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(p.Second, NumberStyles.Float, CultureInfo.InvariantCulture, out float y) && MathF.Abs(x - y) <= 1e-4f * MathF.Max(1, MathF.Abs(y)));
    }
}
