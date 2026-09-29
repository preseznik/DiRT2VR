using System.Xml.Linq;

internal static class ReplayCameras
{
    internal static XDocument Convert(XDocument source)
    {
        var result = new XDocument(source);
        var root = result.Root ?? throw new InvalidDataException("Missing replay camera configuration.");
        foreach (var camera in root.Elements("Camera").Where(e => (string?)e.Attribute("type") == "helicopter").ToArray())
        {
            if ((string?)camera.Attribute("ident") == (string?)root.Attribute("initialCamera"))
                throw new InvalidDataException("Unsupported initial replay camera.");
            // DiRT 2's camera factory returns null for this DiRT 3-only type.
            // Keep all supported Aspen cameras, their authored transforms and spline paths.
            camera.Remove();
        }
        return result;
    }
}
