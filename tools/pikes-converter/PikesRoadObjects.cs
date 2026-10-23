using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Source traffic furniture converted to native movable objects.
internal static partial class PikesRoadObjects
{
    static readonly Vector3 Origin = new(1556.6499f, 54.7f, -4592.365f);
    static string A(XElement e, string name) => (string?)e.Attribute(name) ?? throw new InvalidDataException("Missing " + name);
    static bool Sign(string name) => name.StartsWith("pke_", StringComparison.Ordinal) && name.Contains("sign", StringComparison.Ordinal);
    static bool Rail(string name) => name.StartsWith("pke_armco_", StringComparison.Ordinal);
    static IEnumerable<PssgElement> Below(PssgElement e) => new[] { e }.Concat(e.ChildElements.SelectMany(Below));
    static Matrix4x4 Matrix(string value)
    {
        var f = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (f.Length != 16 || f.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid furniture transform");
        return new(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
    }
    static Matrix4x4 World(PssgNode n)
    {
        var result = n.Transform.Transform;
        for (var p = n.ParentElement; p != null; p = p.ParentElement) if (p is PssgNode node) result *= node.Transform.Transform;
        return result;
    }
}
