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
    // Smelter diagnostic: retain the source instance IDs and all damage states,
    // but let every ornament's imported visibility record cover the whole venue.
    // This deliberately trades object-level visibility culling for stability;
    // the terrain, tree and light records keep their own bounds.
    internal static object ConservativeObjects(string track)
    {
        string path=Path.Combine(track,"route_0/track.vis");
        var bytes=File.ReadAllBytes(path);
        _=VisibilityAudit.Check(bytes);
        int I(int p)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p,4));
        var nodes=new List<int>(); var records=new List<int>();
        var min=new Vector3(float.MaxValue);var max=new Vector3(float.MinValue);
        int cursor=I(28),end=I(44);
        for(int i=0;i<I(12);i++) {
            if(cursor<0 || (long)cursor+48>end || (I(cursor+28)&65535)!=i)
                throw new InvalidDataException("Invalid Smelter visibility node.");
            nodes.Add(cursor);
            for(int k=0;k<3;k++) {
                float lo=BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(cursor+k*4,4));
                float hi=BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(cursor+16+k*4,4));
                if(!float.IsFinite(lo) || !float.IsFinite(hi) || lo>hi) throw new InvalidDataException("Invalid visibility bounds.");
                min[k]=Math.Min(min[k],lo);max[k]=Math.Max(max[k],hi);
            }
            int count=(int)((uint)I(cursor+28)>>16),next=checked(cursor+48+count*32);
            if(next>end || I(cursor+32)!=(i==I(12)-1 ? 0 : next)) throw new InvalidDataException("Invalid visibility span.");
            for(int j=0;j<count;j++) {int record=cursor+48+j*32;if(I(record+12)==2) records.Add(record);}
            cursor=next;
        }
        if(cursor!=end || records.Count==0) throw new InvalidDataException("Missing Smelter ornament visibility records.");
        // Expand parents too: no descendant should be rejected by an old cell box.
        foreach(int at in nodes.Concat(records))
            for(int k=0;k<3;k++) {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at+k*4,4),min[k]);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at+16+k*4,4),max[k]);
            }
        _=VisibilityAudit.Check(bytes);
        var ornamentPath=Path.Combine(track,"route_0/ornaments.bin");
        var ornaments=File.ReadAllBytes(ornamentPath);
        int O(int p)=>BinaryPrimitives.ReadInt32LittleEndian(ornaments.AsSpan(p,4));
        var entities=PortFiles.ReadPssg(Path.Combine(track,"route_0/objects.ens"))
            .Descendants("TEMPLATEENTITYINSTANCE").ToLookup(e=>((string)e.Attribute("uri")!)[1..],StringComparer.Ordinal);
        var corners=(from x in new[]{min.X,max.X} from y in new[]{min.Y,max.Y} from z in new[]{min.Z,max.Z} select new Vector3(x,y,z)).ToArray();
        Matrix4x4 Matrix(float[] v)=>new(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9],v[10],v[11],v[12],v[13],v[14],v[15]);
        var modelReports=new List<object>();int entityCount=0,staticCount=0;
        // Most movable Smelter props have no source VIS record. DiRT 2 inserts
        // those into a runtime spatial tree using the model bounds in ornaments.
        // Enclose the venue for every live instance, in that model's local space.
        for(int i=0;i<O(40);i++) {
            int at=O(36)+i*56,start=O(at),endName=Array.IndexOf(ornaments,(byte)0,start);
            string name=Encoding.ASCII.GetString(ornaments,start,endName-start);
            var transforms=new List<Matrix4x4>();
            int count=O(at+48),data=O(at+44);
            for(int j=0;j<count;j++) {
                var v=new float[16];v[15]=1;int[] fields=[0,1,2,4,5,6,8,9,10,12,13,14];
                for(int k=0;k<12;k++) v[fields[k]]=BinaryPrimitives.ReadSingleLittleEndian(ornaments.AsSpan(data+j*64+k*4,4));
                transforms.Add(Matrix(v));
            }
            foreach(var entity in entities[name]) {
                var v=entity.Element("TEMPLATETRANSFORM")!.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)
                    .Select(n=>float.Parse(n,CultureInfo.InvariantCulture)).ToArray();
                if(v.Length!=16 || v.Any(f=>!float.IsFinite(f))) throw new InvalidDataException("Invalid entity transform.");
                transforms.Add(Matrix(v));
            }
            if(transforms.Count==0) continue;
            var lo=new Vector3(BinaryPrimitives.ReadSingleLittleEndian(ornaments.AsSpan(at+4)),BinaryPrimitives.ReadSingleLittleEndian(ornaments.AsSpan(at+8)),BinaryPrimitives.ReadSingleLittleEndian(ornaments.AsSpan(at+12)));
            var hi=new Vector3(BinaryPrimitives.ReadSingleLittleEndian(ornaments.AsSpan(at+16)),BinaryPrimitives.ReadSingleLittleEndian(ornaments.AsSpan(at+20)),BinaryPrimitives.ReadSingleLittleEndian(ornaments.AsSpan(at+24)));
            foreach(var transform in transforms) {
                if(!Matrix4x4.Invert(transform,out var inverse)) throw new InvalidDataException("Singular ornament transform.");
                foreach(var corner in corners) {var point=Vector3.Transform(corner,inverse);lo=Vector3.Min(lo,point);hi=Vector3.Max(hi,point);}
            }
            lo-=Vector3.One;hi+=Vector3.One;
            for(int k=0;k<3;k++) {
                if(!float.IsFinite(lo[k]) || !float.IsFinite(hi[k])) throw new InvalidDataException("Nonfinite ornament bounds.");
                BinaryPrimitives.WriteSingleLittleEndian(ornaments.AsSpan(at+4+k*4,4),lo[k]);
                BinaryPrimitives.WriteSingleLittleEndian(ornaments.AsSpan(at+16+k*4,4),hi[k]);
            }
            int dynamicCount=transforms.Count-count;entityCount+=dynamicCount;staticCount+=count;
            modelReports.Add(new {Model=name,NativeStaticPlacements=count,MovablePlacements=dynamicCount});
        }
        if(entityCount!=entities.Sum(g=>g.Count())) throw new InvalidDataException("Unaccounted native entity bounds.");
        File.WriteAllBytes(ornamentPath,ornaments);
        File.WriteAllBytes(path,bytes);
        return new { OrnamentRecords=records.Count,SpatialNodes=nodes.Count,NativeModels=modelReports,
            NativeStaticPlacements=staticCount,MovablePlacements=entityCount,
            Bounds=new[]{min.X,min.Y,min.Z,max.X,max.Y,max.Z},
            Mode="Whole-venue ornament visibility bounds",InstanceIdsAndGeometryPreserved=true,RuntimeValidated=false };
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
