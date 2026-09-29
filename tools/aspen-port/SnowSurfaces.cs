using System.Globalization;
using System.Xml.Linq;

internal static class SnowSurfaces
{
    // Initial DiRT 2-compatible calibration; runtime handling is an acceptance gate.
    // Keep the engine's known tyre tables/effect names, never import DiRT 3 mechanics verbatim.
    internal record Surface(string Donor, double Grip, double Friction, double Slowdown);
    internal static readonly Dictionary<string, Surface> Recipes = new()
    {
        ["SNO"] = new("LDG", .85, .30, .12),
        ["SWC"] = new("LDG", 1.05, .35, .04),
        ["SWS"] = new("MWG", .70, .25, .20),
        ["SNB"] = new("GBK", .35, .25, 2.0),
        ["DSC"] = new("LDG", 1.05, .35, .04),
        ["DSN"] = new("LDG", .85, .30, .12),
        ["ICS"] = new("SWT", .25, .08, 0),
        ["TSD"] = new("SDT", 2.0, .55, 0),
        ["TSW"] = new("SWT", 1.7, .40, 0),
        ["RKW"] = new("WRK", 1.65, .35, 0),
        ["MRD"] = new("MET", 2.2, .55, 0)
    };
    internal static XDocument Create(XDocument original, IEnumerable<string> required)
    {
        var result = new XDocument(original);
        foreach (var code in required.Distinct().Order())
        {
            if (code.Length != 4 || code[3] is not ('+' or '*') || !Recipes.TryGetValue(code[..3], out var recipe))
                throw new InvalidDataException("Unsupported Aspen collision surface: " + code);
            if (result.Descendants("MATERIAL").Any(e => (string?)e.Attribute("name") == code))
                throw new InvalidDataException("Custom surface already exists; source preserved: " + code);
            var donor = result.Descendants("MATERIAL").Single(e => (string?)e.Attribute("name") == recipe.Donor + code[3]);
            var material = new XElement(donor);
            material.SetAttributeValue("name", code);
            material.SetAttributeValue("friction", recipe.Friction.ToString("0.000", CultureInfo.InvariantCulture));
            var mechanics = material.Element("MECHANICS") ?? throw new InvalidDataException("Missing material mechanics.");
            mechanics.SetAttributeValue("grip", recipe.Grip.ToString("0.000", CultureInfo.InvariantCulture));
            mechanics.SetAttributeValue("slowdown", recipe.Slowdown.ToString("0.000", CultureInfo.InvariantCulture));
            donor.Parent!.Add(material);
        }
        return result;
    }
}
