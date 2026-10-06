using System.Xml.Linq;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

internal static class SmelterVisibility
{
    internal static void Route(XDocument route)
    {
        var blocks = route.Root!.Elements().Where(n => n.Attribute("track_lod_dist") is not null).ToArray();
        if (blocks.Length == 0 || route.Root.Element("default")?.Attribute("track_lod_dist") is null || blocks.Any(n => n.Attribute("track_cull_dist") is null || n.Attribute("world_cull_dist") is null || n.Attribute("main_obj_size") is null))
            throw new InvalidDataException("Unexpected Smelter visibility controls.");
        // Baked scenery lives in the terrain tiles, so their culling distances
        // must cover the venue too. Keep shadow/reflection distances unchanged.
        foreach (var block in blocks)
        {
            block.SetAttributeValue("track_lod_dist", "1800.0");
            block.SetAttributeValue("track_cull_dist", "2000.0");
            block.SetAttributeValue("world_cull_dist", "2000.0");
            block.SetAttributeValue("main_obj_size", "0.0");
        }
    }
    // Cover every referenced model, including names absent from the source XML.
    // The previous barrier-name filter missed fence variants and small props.
    internal static object Objects(string track, string d2, string output, Dictionary<string,string> inputs)
    {
        var ornaments = File.ReadAllBytes(Path.Combine(track,"route_0/ornaments.bin"));
        int I(int p) => BinaryPrimitives.ReadInt32LittleEndian(ornaments.AsSpan(p,4));
        int table=I(36), count=I(40), slots=I(28);
        if(table<0 || count<1 || (long)table+count*56>ornaments.Length || slots<1 || slots>2048)
            throw new InvalidDataException("Unexpected Smelter ornament inventory.");
        var names = new List<string>(); int capacity=0;
        for(int i=0;i<count;i++) {
            int at=table+i*56, start=I(at), end=Array.IndexOf(ornaments,(byte)0,start);
            if(start<0 || end<=start || I(at+36)<0) throw new InvalidDataException("Invalid ornament reference.");
            capacity=checked(capacity+I(at+36));
            if(I(at+36)>0) names.Add(Encoding.ASCII.GetString(ornaments,start,end-start));
        }
        if(capacity!=slots || names.Distinct(StringComparer.Ordinal).Count()!=names.Count)
            throw new InvalidDataException("Smelter ornament capacity mismatch.");
        var path=Path.Combine(track,"route_0/ornament_attributes.xml");
        var attributes=PortFiles.ReadXml(path);
        var models=attributes.Root!.Elements("ornament_attributes").ToDictionary(m=>(string)m.Attribute("name")!,StringComparer.Ordinal);
        int added=0, changed=0;
        foreach(string name in names) {
            if(!models.TryGetValue(name,out var model)) {
                model=new XElement("ornament_attributes",new XAttribute("name",name),new XAttribute("key_feature","0"),new XAttribute("envmap_only_feature","0"));
                attributes.Root.Add(model);added++;
            }
            if((string?)model.Attribute("envmap_only_feature")=="1") continue;
            model.SetAttributeValue("lod_distance_00","1800.00");
            model.SetAttributeValue("lod_distance_01","1900.00");
            model.SetAttributeValue("lod_distance_02","2000.00");
            changed++;
        }
        Write(attributes,path);

        // A private session style sizes the main view before DiRT 2 allocates
        // traversal buffers. Include the reserved baked slots conservatively;
        // preserve the source style's shadow/reflection budgets and all stock styles.
        var routePath=Path.Combine(track,"route_0/route_overrides.xml");
        var route=PortFiles.ReadXml(routePath);
        var systems=route.Root!.Elements("Systems").Single();
        string style=(string?)systems.Attribute("ornament_settings") ?? throw new InvalidDataException("Missing ornament style.");
        var source=Path.Combine(d2,"tracks/ornament_system_settings.xml");inputs[source]=PortFiles.Hash(source);
        var settings=PortFiles.ReadXml(source);
        var copy=new XElement(settings.Root!.Elements("setting_style").Single(s=>(string?)s.Attribute("name")==style));
        const string privateStyle="d2vr_smelter";
        if(settings.Root.Elements("setting_style").Any(s=>(string?)s.Attribute("name")==privateStyle)) throw new InvalidDataException("Smelter ornament style already exists.");
        var main=copy.Elements("traversal_settings").Single(t=>(string?)t.Attribute("name")=="main_scene");
        int oldCapacity=(int)main.Attribute("instance_buffer")!, budget=Math.Max(oldCapacity,2048);
        main.SetAttributeValue("instance_buffer",budget);copy.SetAttributeValue("name",privateStyle);settings.Root.Add(copy);
        systems.SetAttributeValue("ornament_settings",privateStyle);
        Write(route,routePath);Write(settings,Path.Combine(output,"ornament_system_settings.xml"));
        return new { ReferencedModels=names.Count, AddedDistanceDefinitions=added, ExtendedModels=changed,
            ReservedInstances=slots, OriginalMainSceneCapacity=oldCapacity, MainSceneCapacity=budget,
            SourceStyle=style, StockStylesPreserved=true, RuntimeValidated=false };
    }
    static void Write(XDocument doc,string path)
    {
        var temporary=path+".visibility-tmp";
        PortFiles.WriteXml(doc,temporary,EgoEngineLibrary.Xml.XmlType.BinXml);
        if(!XNode.DeepEquals(doc.Root,PortFiles.ReadXml(temporary).Root)) throw new InvalidDataException("Smelter visibility serialization changed.");
        File.Move(temporary,path,true);
    }
    internal static int SceneBounds(XDocument scene)
    {
        float[] Values(XElement box) => box.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
        int changed=0;
        // Static baking expanded previously empty parent boxes using only the
        // added props. Refit bottom-up to include their original terrain too.
        foreach(var node in scene.Descendants().Where(n=>n.Element("BOUNDINGBOX") is not null).Reverse()) {
            var box=node.Element("BOUNDINGBOX")!;var bounds=Values(box);
            if(bounds.Length!=6 || bounds.Any(v=>!float.IsFinite(v))) throw new InvalidDataException("Invalid scene bounds.");
            var before=bounds.ToArray();
            foreach(var child in node.Elements().Where(n=>n.Element("BOUNDINGBOX") is not null && (n.Name=="RENDERNODE" || n.Descendants("RENDERNODE").Any()))) {
                if(child.Element("TRANSFORM") is { } transform) {
                    var f=Values(transform);
                    if(f.Length!=16 || new Matrix4x4(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15])!=Matrix4x4.Identity)
                        throw new InvalidDataException("Nonidentity Smelter terrain transform.");
                }
                var b=Values(child.Element("BOUNDINGBOX")!);
                if(b.Length!=6 || b.Any(v=>!float.IsFinite(v))) throw new InvalidDataException("Invalid child bounds.");
                for(int k=0;k<3;k++) {bounds[k]=Math.Min(bounds[k],b[k]);bounds[k+3]=Math.Max(bounds[k+3],b[k+3]);}
            }
            if(!before.SequenceEqual(bounds)) {box.Value=string.Join(' ',bounds.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));changed++;}
        }
        return changed;
    }
}
