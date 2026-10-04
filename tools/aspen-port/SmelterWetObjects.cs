using System.Xml.Linq;

internal static class SmelterWetObjects
{
    // The wet2 overlays reference the ordinary building placements. Merge their
    // resources under unique IDs so the static bake can preserve those meshes.
    internal static void Merge(string track)
    {
        string path = Path.Combine(track,"objects.pssg");
        var objects = PortFiles.ReadPssg(path);
        var wet = PortFiles.ReadPssg(Path.Combine(track,"objects_wet.pssg"));
        var known = objects.Descendants().Attributes("id").Select(a=>a.Value).ToHashSet(StringComparer.Ordinal);
        var remap = wet.Descendants().Where(n=>n.Attribute("id") is not null && n.Name != "SHADERGROUP")
            .ToDictionary(n=>(string)n.Attribute("id")!,n=>n.Name == "ROOTNODE" ? (string)n.Attribute("id")! : "smelter_wet_"+(string)n.Attribute("id")!,StringComparer.Ordinal);
        if(remap.Values.Any(known.Contains)) throw new InvalidDataException("Wet object identity conflicts with ordinary scenery.");
        foreach(var attribute in wet.Descendants().Attributes())
            if(attribute.Name == "id" && remap.TryGetValue(attribute.Value,out var id)) attribute.Value=id;
            else if(attribute.Value.StartsWith('#') && remap.TryGetValue(attribute.Value[1..],out var reference)) attribute.Value="#"+reference;
        foreach(var library in wet.Descendants("LIBRARY"))
        {
            string type=(string)library.Attribute("type")!;
            var target=objects.Descendants("LIBRARY").SingleOrDefault(n=>(string?)n.Attribute("type")==type);
            if(target is null) {target=new XElement("LIBRARY",new XAttribute("type",type));objects.Root!.Element("PSSGDATABASE")!.Add(target);}
            foreach(var child in library.Elements())
            {
                var existing=target.Elements().SingleOrDefault(n=>(string?)n.Attribute("id")== (string?)child.Attribute("id"));
                if(existing is not null) {
                    if(child.Name != "SHADERGROUP" || !XNode.DeepEquals(existing,child)) throw new InvalidDataException("Conflicting wet shader definition.");
                    continue;
                }
                target.Add(new XElement(child));
            }
        }
        PortFiles.WritePssg(objects,path+".wet.tmp");
        ObjectVertexLayout.Verify(objects,PortFiles.ReadPssg(path+".wet.tmp"));
        File.Move(path+".wet.tmp",path,true);
    }
}
