using System.Globalization;
using System.Xml.Linq;

// Glow and emissive lamp faces must not cross-fade between HIGH/LOW terrain.
internal static class PersistentLights
{
    static float[] Bounds(XElement node) => node.Element("BOUNDINGBOX")!.Value
        .Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();

    static string DrawKey(XElement draw)
    {
        var copy=new XElement(draw); copy.Attribute("id")!.Remove();
        return copy.ToString(SaveOptions.DisableFormatting);
    }

    internal static object Convert(string track)
    {
        var objects=PortFiles.ReadPssg(Path.Combine(track,"objects.pssg"));
        var groups=objects.Descendants("SHADERINSTANCE").ToDictionary(s=>"#"+(string)s.Attribute("id")!,s=>(string)s.Attribute("shaderGroup")!);
        var selected=new HashSet<string>();
        foreach(var lod in objects.Descendants("LODVISIBLERENDERNODE"))
        {
            var materials=lod.Elements("RENDERSTREAMINSTANCE").Select(i=>(string)i.Attribute("shader")!).ToArray();
            if(!materials.Any(m=>groups[m]=="#volumetrics.fx")) continue;
            foreach(var m in materials.Where(m=>groups[m] is "#volumetrics.fx" or "#object_simple_emissive.fx"))
                selected.Add("#aspen_static_"+m[1..]);
        }
        string path=Path.Combine(track,"land.pssg");
        var scene=PortFiles.ReadPssg(path); var before=new XDocument(scene);
        int moved=0;
        foreach(var high in scene.Descendants("RENDERNODE").Where(n=>((string?)n.Attribute("id"))?.StartsWith("HIGH_",StringComparison.Ordinal)==true).ToArray())
        {
            var draws=high.Elements("RENDERSTREAMINSTANCE").Where(i=>selected.Contains((string)i.Attribute("shader")!)).ToArray();
            if(draws.Length==0) continue;
            string suffix=((string)high.Attribute("id")!)[5..];
            var low=high.Parent!.Elements("RENDERNODE").Single(n=>(string?)n.Attribute("id")=="LOW_"+suffix);
            var nonlod=high.Parent.Elements("RENDERNODE").SingleOrDefault(n=>(string?)n.Attribute("id")=="NONLOD_"+suffix);
            if(nonlod is null)
            {
                nonlod=new XElement("RENDERNODE",high.Attributes().Select(a=>new XAttribute(a)),new XElement(high.Element("TRANSFORM")!),new XElement(high.Element("BOUNDINGBOX")!));
                nonlod.SetAttributeValue("id","NONLOD_"+suffix); nonlod.SetAttributeValue("nickname","NONLOD_"+suffix);
                low.AddAfterSelf(nonlod);
            }
            if(!XNode.DeepEquals(high.Element("TRANSFORM"),low.Element("TRANSFORM")) || !XNode.DeepEquals(high.Element("TRANSFORM"),nonlod.Element("TRANSFORM")))
                throw new InvalidDataException("Terrain light layer transforms differ.");
            var a=Bounds(nonlod); var b=Bounds(high);
            nonlod.Element("BOUNDINGBOX")!.Value=string.Join(' ',Enumerable.Range(0,6).Select(i=>(i<3?Math.Min(a[i],b[i]):Math.Max(a[i],b[i])).ToString("R",CultureInfo.InvariantCulture)));
            foreach(var draw in draws)
            {
                string key=DrawKey(draw);
                // Both references point at one physical light. Keep one draw.
                low.Elements("RENDERSTREAMINSTANCE").Single(i=>DrawKey(i)==key).Remove();
                draw.Remove(); nonlod.Add(draw); moved++;
            }
        }
        if(moved==0) throw new InvalidDataException("No Aspen tower light draws found.");
        PortFiles.WritePssg(scene,path+".tmp");
        ObjectVertexLayout.Verify(before,PortFiles.ReadPssg(path+".tmp"));
        File.Move(path+".tmp",path,true);
        return new { Draws=moved, OriginalMeshesAndMaterialsPreserved=true, DuplicateLodReferencesConsolidated=true };
    }
}
