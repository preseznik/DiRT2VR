using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

// GRID 2 ends a braking section at another gate. DiRT 2 instead holds the
// requested speed for a duration. Keep the authored line and speed range.
internal static class SpaAi
{
    internal sealed record Brake(int Id, int Gate, int HoldGate, float DistanceMetres,
        float MinimumSpeed, float MaximumSpeed, float HoldSeconds);

    internal static Brake[] ConvertBrakes(XElement track)
    {
        var gates = track.Element("gates")!.Elements("gate").ToDictionary(g => (int)g.Attribute("id")!);
        var next = track.Element("links")!.Elements("link").ToDictionary(l => (int)l.Attribute("from_gate")!, l => (int)l.Attribute("to_gate")!);
        var holds = track.Element("hold_lines")!.Elements("hold_line").ToDictionary(h => (int)h.Attribute("id")!);
        var source = track.Element("brake_lines")!.Elements("brake_line").ToArray();
        var converted = new List<XElement>();
        var report = new List<Brake>();
        var ids = new HashSet<int>();
        foreach (var brake in source)
        {
            int id = (int)brake.Attribute("id")!, start = (int)brake.Attribute("gate_id")!;
            var data = brake.Element("brake_data")!;
            if (!ids.Add(id) || (string?)data.Attribute("type") != "normal" ||
                !holds.TryGetValue((int)data.Attribute("hold_line_id")!, out var hold) ||
                (int)hold.Attribute("brakeline_id")! != id)
                throw new InvalidDataException("Unsupported Spa braking section: " + id);
            int end = (int)hold.Attribute("gate_id")!;
            float minimum = Number(data, "min_speed"), maximum = Number(data, "max_speed");
            float modifier = Number(data, "distance_modifier"), curve = Number(data, "curve_modifier");
            if (minimum <= 0 || maximum < minimum || modifier <= 0 || curve <= 0 ||
                start == end || !gates.ContainsKey(start) || !gates.ContainsKey(end))
                throw new InvalidDataException("Invalid Spa braking range: " + id);
            float distance = 0;
            int at = start;
            var visited = new HashSet<int>();
            while (at != end)
            {
                if (!visited.Add(at) || !next.TryGetValue(at, out int to) || !gates.ContainsKey(to))
                    throw new InvalidDataException("Spa braking section cannot reach its hold gate: " + id);
                distance += Vector3.Distance(LinePoint(gates[at]), LinePoint(gates[to]));
                at = to;
            }
            float seconds = distance / minimum;
            if (!float.IsFinite(seconds) || seconds <= 0)
                throw new InvalidDataException("Invalid Spa braking duration: " + id);
            converted.Add(new XElement("brake_line", new XAttribute("id", id), new XAttribute("gate_id", start),
                new XElement("brake_data", new XAttribute("type", "normal"),
                    new XAttribute("min_speed", F(minimum)), new XAttribute("max_speed", F(maximum)),
                    new XAttribute("hold_time", F(seconds)), new XAttribute("distance_modifier", F(modifier)),
                    new XAttribute("curve_modifier", F(curve)))));
            report.Add(new(id, start, end, distance, minimum, maximum, seconds));
        }
        if (source.Length == 0 || holds.Count != source.Length)
            throw new InvalidDataException("Spa source braking sections are incomplete.");
        // Do not silently discard a future nonempty control section.
        foreach (string name in new[] { "speed_lines", "min_speed_lines", "retire_lines", "fork_sets" })
            if (track.Element(name)?.HasElements == true)
                throw new InvalidDataException("Unsupported Spa AI controls: " + name);
        track.Element("brake_lines")!.ReplaceWith(new XElement("brake_lines", new XAttribute("num_brake_lines", converted.Count), converted));
        track.Element("hold_lines")!.Remove();
        track.Element("speed_lines")?.Remove();
        return report.ToArray();
    }

    static Vector3 LinePoint(XElement gate)
    {
        Vector3 Vector(string name)
        {
            var v = gate.Element(name)!.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (v.Length != 3 || v.Any(n => !float.IsFinite(n))) throw new InvalidDataException("Invalid Spa AI vector.");
            return new(v[0], v[1], v[2]);
        }
        var line = gate.Element("waypoints")!.Elements("waypoint")
            .Single(w => (string?)w.Element("racing_line")?.Attribute("type") == "optimal");
        return Vector("position") + Vector("normal") * Number(line, "length");
    }
    static float Number(XElement element, string name)
    {
        float value = float.Parse((string)element.Attribute(name)!, CultureInfo.InvariantCulture);
        return float.IsFinite(value) ? value : throw new InvalidDataException("Invalid Spa AI value: " + name);
    }
    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
