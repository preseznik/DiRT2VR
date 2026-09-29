using System.Xml.Linq;

// DiRT 2's gate reader requires an edges collection even when it is empty.
// DiRT 3 omits it and adds a visual-only line unknown to DiRT 2's line enum.
internal static class AiTrack
{
    internal static void Convert(XDocument document)
    {
        if (document.Root?.Name != "ai_track_data") throw new InvalidDataException("Expected Aspen AI track data.");
        var tracks = document.Root.Elements("track").ToArray();
        if (tracks.Length == 0) throw new InvalidDataException("Aspen AI track is empty.");
        foreach (var track in tracks)
        {
            var gates = track.Element("gates")?.Elements("gate").ToArray()
                ?? throw new InvalidDataException("Aspen AI gates are missing.");
            if (gates.Length == 0) throw new InvalidDataException("Aspen AI gates are empty.");
            foreach (var gate in gates)
            {
                var waypoints = gate.Element("waypoints") ?? throw new InvalidDataException("Aspen AI waypoints are missing.");
                foreach (var point in waypoints.Elements("waypoint").Where(p => (string?)p.Element("racing_line")?.Attribute("type") == "visual").ToArray())
                    point.Remove();
                waypoints.SetAttributeValue("num_waypoints", waypoints.Elements("waypoint").Count());
                if (!waypoints.Elements("waypoint").Any(p => (string?)p.Element("racing_line")?.Attribute("type") == "optimal"))
                    throw new InvalidDataException("Aspen AI driving line is missing.");
                if (gate.Element("edges") is null) gate.Add(new XElement("edges", new XAttribute("num_edges", 0)));
            }
        }
    }
}
