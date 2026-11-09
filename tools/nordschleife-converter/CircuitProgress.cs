using System.Globalization;
using System.Xml;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Nordschleife;

public static class CircuitProgress
{
    public static byte[] Patch(byte[] original)
    {
        using var input = new MemoryStream(original);
        var binary = new XmlFile(input);
        var document = binary.Document;
        var track = document.SelectSingleNode("/progress_track_data/track") as XmlElement;
        var route = document.SelectSingleNode("/progress_track_data/routes[@num_routes='1']/route[@id='0'][@num_splits='10']") as XmlElement;
        var gates = document.SelectNodes("/progress_track_data/gates/gate")!.Cast<XmlElement>().ToArray();
        var splits = document.SelectNodes("/progress_track_data/routes/route/split")!.Cast<XmlElement>().ToArray();
        if (track?.GetAttribute("type") != "point_to_point" || route == null || gates.Length is < 100 or > 256 ||
            splits.Length != 10 || splits.Any(split => split.ParentNode != route) ||
            splits[0].GetAttribute("type") != "finish" || splits[0].GetAttribute("gate") != "1" ||
            splits[9].GetAttribute("type") != "finish" || splits[9].GetAttribute("gate") != (gates.Length - 1).ToString(CultureInfo.InvariantCulture) ||
            splits.Skip(1).Take(8).Any(split => split.GetAttribute("type") != "time") ||
            !SameEndpoint("left") || !SameEndpoint("right"))
            throw new IOException("Unsupported Nordschleife progress route; files preserved. Rebuild this pack from source.");

        // A closed lap has one finish identity. The authored open route repeats it at the end.
        track.SetAttribute("type", "circuit");
        route.RemoveChild(splits[9]);
        route.SetAttribute("num_splits", "9");
        using var output = new MemoryStream();
        binary.Write(output, XmlType.BinXml);
        return output.ToArray();

        bool SameEndpoint(string side) => gates[1][side]?.InnerText is { Length: > 0 } first && gates[^1][side]?.InnerText == first;
    }
}
