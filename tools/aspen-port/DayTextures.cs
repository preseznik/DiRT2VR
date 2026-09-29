using System.Xml.Linq;

// DiRT 3 overlays day/route texture packs at runtime. DiRT 2 otherwise sees
// their 4x4 black placeholders in the resident texture bundles.
internal static class DayTextures
{
    internal static object Resolve(string track, AspenCondition? condition = null)
    {
        condition ??= AspenCondition.Morning;
        string[] packs=[$"ambientmaps_{condition.Suffix}.pssg","trackao_day.pssg",$"route_0/route_trackao_{condition.Suffix}.pssg"];
        var replacements=new Dictionary<string,XElement>(StringComparer.Ordinal);
        foreach(var relative in packs)
            foreach(var texture in PortFiles.ReadPssg(Path.Combine(track,relative)).Descendants("TEXTURE"))
                replacements[(string)texture.Attribute("id")!]=new XElement(texture);
        var report=new List<object>();
        foreach(var path in Directory.EnumerateFiles(track,"*.pssg",SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative=Path.GetRelativePath(track,path).Replace('\\','/');
            if(packs.Contains(relative)) continue;
            var doc=PortFiles.ReadPssg(path); var changed=new List<string>();
            foreach(var texture in doc.Descendants("TEXTURE").ToArray())
            {
                string id=(string)texture.Attribute("id")!;
                if(!replacements.TryGetValue(id,out var replacement)) continue;
                if((int)texture.Attribute("width")! != 4 || (int)texture.Attribute("height")! != 4)
                    throw new InvalidDataException("Expected condition texture placeholder: "+relative+"#"+id);
                texture.ReplaceWith(new XElement(replacement)); changed.Add(id);
            }
            if(changed.Count==0) continue;
            PortFiles.WritePssg(doc,path+".tmp"); var saved=PortFiles.ReadPssg(path+".tmp");
            foreach(var id in changed)
                if(!XNode.DeepEquals(doc.Descendants("TEXTURE").Single(t=>(string?)t.Attribute("id")==id),saved.Descendants("TEXTURE").Single(t=>(string?)t.Attribute("id")==id)))
                    throw new InvalidDataException("Condition texture changed during serialization.");
            File.Move(path+".tmp",path,true); report.Add(new { File=relative, Textures=changed });
        }
        return new { Condition=condition.Name, Packs=packs, RouteOverridesCommon=true, Files=report };
    }
}
