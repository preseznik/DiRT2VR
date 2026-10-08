using System.Globalization;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

internal static class MizuParticleLighting
{
    // Empirical Mizu-only calibration for DiRT 2's HDR response.
    // Keep it independent of unlit effects and HDR settings.
    const float NativeRadianceScale=.2f;
    internal static void Write(string grid2,string input,string output,string report)
    {
        static XDocument Read(string path){using var stream=File.OpenRead(path);return XDocument.Parse(new XmlFile(stream).Document.OuterXml);}
        string sourcePath=Files.Inside(grid2,"tracks/locations/p2p/okutama/lighting_day.xml");
        var source=Read(sourcePath);var target=Read(input);
        string Value(XDocument doc,string name)=>doc.Descendants("param").Single(p=>p.Attribute(name) is not null).Attribute(name)!.Value;
        static float Number(string value)=>float.Parse(value,CultureInfo.InvariantCulture);
        float brightness=Number(Value(source,"pfx_sun_brightness"))*NativeRadianceScale,ambient=Number(Value(source,"vehicleAmbientScaleMin"))*NativeRadianceScale;
        string sunlight=string.Join(" ",Value(source,"pfx_sun_colour").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>(Number(v)*brightness).ToString("R",CultureInfo.InvariantCulture)));
        string floor=ambient.ToString("R",CultureInfo.InvariantCulture);string ambientColour=$"{floor} {floor} {floor}";
        var before=new Dictionary<string,string>();
        foreach(var (name,value) in new[]{("pfx_suncolour",sunlight),("pfx_ambient",ambientColour)})
        {
            before[name]=Value(target,name);target.Descendants("param").Single(p=>p.Attribute(name) is not null).SetAttributeValue(name,value);
        }
        using var xml=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(target.ToString()));using var encoded=new MemoryStream();new XmlFile(xml).Write(encoded,XmlType.BinXml);
        File.WriteAllBytes(output,encoded.ToArray());
        Files.Json(report,new{Source=sourcePath,SourceHash=Files.Hash(sourcePath),Before=before,SunColour=sunlight,AmbientColour=ambientColour,NativeRadianceScale,
            SourceSunColourAndBrightness=true,AmbientApproximation="Track-local neutral particle ambient uses the source vehicle ambient floor; spatial particle ambient GI remains approximate",
            UnlitParticlesUnchanged=true,HdrUnchanged=true,RuntimeValidated=false});
    }
}
