using System.Xml.Linq;

internal static class SmelterSurfaces
{
    // DiRT 3 descriptive codes -> DiRT 2 equivalents. Use complete native tyre,
    // audio and particle definitions, including the sheet suffix, without snow tuning.
    internal static readonly IReadOnlyDictionary<string,string> Donors = new Dictionary<string,string> {
        ["DLE"]="DRT", ["GHD"]="HDG", ["GLD"]="LDG", ["GMD"]="MDG",
        ["GSL"]="GRS", ["TRD"]="RDT", ["TSD"]="SDT"
    };
    internal static XDocument Create(XDocument stock, IEnumerable<string> codes)
    {
        var result=new XDocument(stock);
        var original=stock.Descendants("MATERIAL").ToDictionary(m=>(string)m.Attribute("name")!);
        foreach(var code in codes.Distinct().Order()) {
            if(original.ContainsKey(code)) continue;
            if(code.Length!=4 || code[3] is not ('+' or '*') || !Donors.TryGetValue(code[..3],out var donor))
                throw new InvalidDataException("Unsupported Smelter surface: "+code);
            if(!original.TryGetValue(donor+code[3],out var material))
                throw new InvalidDataException("Missing DiRT 2 surface donor: "+donor+code[3]);
            var copy=new XElement(material);copy.SetAttributeValue("name",code);
            result.Descendants("MATERIAL").First().Parent!.Add(copy);
        }
        return result;
    }
}
