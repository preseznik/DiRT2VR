using System.Text.Json;
using System.Xml.Linq;
using System.Globalization;
using System.Text;

internal static class ConditionLighting
{
    internal const string DefinitionsPath = "tracks/light_definitions.xml";
    internal static XDocument GroundLights(XDocument ambient, XDocument lighting)
    {
        float Scale(string name) => float.Parse((string)lighting.Descendants("param").Single(p=>p.Attribute(name) is not null).Attribute(name)!,CultureInfo.InvariantCulture);
        float x=Scale("ambientLightMapXScale")/2, z=Scale("ambientLightMapZScale")/2;
        if(!float.IsFinite(x) || !float.IsFinite(z) || x<=0 || z<=0) throw new InvalidDataException("Invalid Aspen night light-map extent.");
        string coordinates=string.Join(' ',new[]{-x,-z,-x,z,x,-z,x,z,0,1,0,0,1,1,1,0}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)))+'\0';
        var bytes=Encoding.ASCII.GetBytes(coordinates);
        var texture=new XElement(ambient.Descendants("TEXTURE").Single(t=>(string?)t.Attribute("id")=="skymap.tga"));
        texture.SetAttributeValue("id","ground_light_B.tga");
        return new XDocument(new XElement("PSSGFILE",new XElement("PSSGDATABASE",
            new XElement("LIBRARY",new XAttribute("type","PNSTRING"),new XElement("PNSTRING",new XAttribute("id","Coordinates"),new XAttribute("size",bytes.Length),new XElement("DATA",EgoEngineLibrary.Helper.HexHelper.ByteArrayToHexViaLookup32(bytes)))),
            new XElement("LIBRARY",new XAttribute("type","RENDERINTERFACEBOUND"),texture))));
    }
    internal static XDocument AddDefinitions(XDocument stock, XDocument source, XDocument placements)
    {
        var result = new XDocument(stock);
        var allowed = stock.Descendants("Light_Type").Attributes().Select(a => a.Name).ToHashSet();
        foreach (var type in placements.Descendants("type"))
        {
            string name = (string)type.Attribute("name")!;
            string renamed = "D2VR_Aspen_" + name;
            if (result.Descendants("Light_Type").Any(t => (string?)t.Attribute("LightName") == renamed))
                throw new InvalidDataException("Duplicate Aspen light definition: " + name);
            var definition = new XElement(source.Descendants("Light_Type").Single(t => (string?)t.Attribute("LightName") == name));
            foreach (var attribute in definition.Attributes().Where(a => !allowed.Contains(a.Name)).ToArray()) attribute.Remove();
            if ((string?)definition.Attribute("type") is not ("Spot" or "Omni")) throw new InvalidDataException("Unsupported Aspen light type.");
            if (definition.Attribute("Texture") is not null) throw new InvalidDataException("Unresolved projected Aspen light texture.");
            definition.SetAttributeValue("LightName", renamed); result.Root!.Add(definition);
            type.SetAttributeValue("name", renamed);
        }
        return result;
    }

    internal static void Build(string track, string d3, string d2, string session, AspenCondition condition, Dictionary<string,string> inputs)
    {
        int lightCount = 0;
        if (condition.Night)
        {
            string stockPath = Path.Combine(d2, DefinitionsPath), sourcePath = Path.Combine(d3,"tracks/generic/light_definitions.xml");
            inputs[stockPath] = PortFiles.Hash(stockPath); inputs[sourcePath] = PortFiles.Hash(sourcePath);
            string placementPath = Path.Combine(track,"route_0/light_placement.xml");
            var placements = PortFiles.ReadXml(placementPath);
            var definitions = AddDefinitions(PortFiles.ReadXml(stockPath), PortFiles.ReadXml(sourcePath), placements);
            lightCount = placements.Descendants("instance").Count();
            placements.Save(placementPath);
            string destination = Path.Combine(session,DefinitionsPath); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            definitions.Save(destination);
            string manifestPath = Path.Combine(session,"effects.json");
            var manifest = JsonSerializer.Deserialize<EffectsTransaction.Manifest>(File.ReadAllBytes(manifestPath))!;
            PortFiles.Json(manifestPath, new EffectsTransaction.Manifest(1,[..manifest.Files,new(DefinitionsPath,PortFiles.Hash(stockPath),PortFiles.Hash(destination))]));

            // DiRT 3 uses different headlight controls. Retain its night light
            // colours and use the native DiRT 2 brightness control for the STI.
            string donorPath = Path.Combine(d2,"tracks/london/battersea/night_lighting.xml");
            inputs[donorPath] = PortFiles.Hash(donorPath);
            var lighting = PortFiles.ReadXml(Path.Combine(track,"lighting.xml"));
            // DiRT 2's night terrain path loads this named map and projection;
            // DiRT 3 keeps the equivalent image in its condition ambient bundle.
            var groundLights=GroundLights(PortFiles.ReadPssg(Path.Combine(track,"ambientmaps_night.pssg")),lighting);
            string groundPath=Path.Combine(track,"baked_lights.pssg");
            PortFiles.WritePssg(groundLights,groundPath);
            var savedGround=PortFiles.ReadPssg(groundPath);
            string Data(XDocument doc)=>string.Concat(doc.Descendants("PNSTRING").Single().Element("DATA")!.Value.Where(c=>!char.IsWhiteSpace(c)));
            if(Data(groundLights)!=Data(savedGround))throw new InvalidDataException("Night projection changed during serialization.");
            if (!lighting.Descendants("param").Any(p => p.Attribute("headLightBrightness") is not null))
                lighting.Root!.Add(new XElement(PortFiles.ReadXml(donorPath).Descendants("param").Single(p => p.Attribute("headLightBrightness") is not null)));
            PortFiles.WriteXml(lighting,Path.Combine(track,"lighting.xml.tmp"),EgoEngineLibrary.Xml.XmlType.BinXml);
            File.Move(Path.Combine(track,"lighting.xml.tmp"),Path.Combine(track,"lighting.xml"),true);
            PortFiles.CopyNew(Path.Combine(track,"lighting.xml"),Path.Combine(track,"night_lighting.xml"));
            var post = Path.Combine(d2,"tracks/london/battersea/night_effects.xml"); inputs[post] = PortFiles.Hash(post);
            PortFiles.CopyNew(post,Path.Combine(track,"night_effects.xml"));
        }
        PortFiles.Json(Path.Combine(Path.GetDirectoryName(track)!,"condition.json"),new {
            Profile=condition.Name,SourceSuffix=condition.Suffix,Headlights=condition.Night,TrackLights=condition.Night,
            RegisteredLights=lightCount,DaylightSunShadowOmitted=condition.Night,ActiveSnowfallImplemented=false,RuntimeValidated=false
        });
    }
}
