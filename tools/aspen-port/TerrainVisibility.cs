using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

// Baked props no longer have their original PVS membership. Keep spatial and
// frustum culling, rebuild their tile bounds, and use conservative region masks.
internal static class TerrainVisibility
{
    internal readonly record struct Box(Vector3 Min, Vector3 Max)
    {
        internal Box Union(Box b) => new(Vector3.Min(Min,b.Min),Vector3.Max(Max,b.Max));
        internal bool Contains(Box b, float epsilon = .01f)
        {
            for(int k=0;k<3;k++) if(!(Min[k]-epsilon <= b.Min[k] && Max[k]+epsilon >= b.Max[k])) return false;
            return true;
        }
        internal bool Near(Box b) => Contains(b) && b.Contains(this);
    }
    static Box[] Tiles(string path)
    {
        var doc = PortFiles.ReadPssg(path);
        return doc.Descendants("NODE").Where(n => ((string?)n.Attribute("id")) is { } id && (id.StartsWith("ROOT_") || id.StartsWith("LAND_")))
            .Select(n => n.Elements("RENDERNODE").Select(r => {
                var b = r.Element("BOUNDINGBOX")!.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
                return new Box(new(b[0],b[1],b[2]),new(b[3],b[4],b[5]));
            }).Where(b => b.Min != Vector3.Zero || b.Max != Vector3.Zero).Aggregate((a,b) => a.Union(b)))
            // Verified runtime sibling order is the reverse of file order.
            .Reverse().ToArray();
    }
    internal static Dictionary<int,Dictionary<int,int>> Mappings(JsonElement reports) => reports.EnumerateArray().ToDictionary(
        r => r.GetProperty("File").GetString()=="trees.bin" ? 3 : 2,
        r => r.GetProperty("Mapping").EnumerateArray().ToDictionary(e=>e.GetProperty("SourceId").GetInt32(),e=>e.GetProperty("TargetId").GetInt32()));
    internal static object Convert(string track, string original, Dictionary<int,Dictionary<int,int>>? mappings = null,string sourceRoute="route_0")
    {
        var before = Tiles(Path.Combine(original,"tracksplit.pssg")).Concat(Tiles(Path.Combine(original,sourceRoute,"routesplit.pssg"))).ToArray();
        var after = Tiles(Path.Combine(track,"land.pssg")).Concat(Tiles(Path.Combine(track,"route_0/routesplit.pssg"))).ToArray();
        var path = Path.Combine(track,"route_0/track.vis"); var bytes = File.ReadAllBytes(path);
        var result = Rebuild(bytes,before,after,mappings); File.WriteAllBytes(path,bytes); return result;
    }
    internal static object Rebuild(byte[] bytes, Box[] before, Box[] after, Dictionary<int,Dictionary<int,int>>? mappings = null)
    {
        _ = VisibilityAudit.Check(bytes);
        int I(int p) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p,4));
        Box Read(int p) => new(new(BitConverter.ToSingle(bytes,p),BitConverter.ToSingle(bytes,p+4),BitConverter.ToSingle(bytes,p+8)),new(BitConverter.ToSingle(bytes,p+16),BitConverter.ToSingle(bytes,p+20),BitConverter.ToSingle(bytes,p+24)));
        var nodes = new List<(int Offset,Box Bounds,int[] Records)>();
        int cursor = I(28), end = I(44);
        if (before.Length != after.Length || before.Length != I(64) || end > bytes.Length) throw new InvalidDataException("Visibility tile inventory mismatch.");
        if(before.Concat(after).Any(b=>Enumerable.Range(0,3).Any(k=>!float.IsFinite(b.Min[k]) || !float.IsFinite(b.Max[k]) || b.Min[k]>b.Max[k]))) throw new InvalidDataException("Invalid terrain bounds.");
        if(mappings is not null && mappings.Values.Any(m=>m.Values.Any(id=>id<0) || m.Values.Distinct().Count()!=m.Count)) throw new InvalidDataException("Placement mapping must be one-to-one.");
        for (int i=0;i<I(12);i++)
        {
            if (cursor < I(28) || (long)cursor+48 > end || (I(cursor+28)&65535) != i) throw new InvalidDataException("Visibility spatial node mismatch.");
            int count=(int)((uint)I(cursor+28)>>16), next=checked(cursor+48+count*32);
            if (next>end || I(cursor+32)!=(i==I(12)-1 ? 0 : next)) throw new InvalidDataException("Visibility spatial span mismatch.");
            var records=Enumerable.Range(0,count).Select(r=>cursor+48+r*32).ToArray(); var bounds=Read(cursor);
            if(records.Any(r=>!bounds.Contains(Read(r)))) throw new InvalidDataException("Visibility record escapes its parent bounds.");
            nodes.Add((cursor,bounds,records)); cursor=next;
        }
        if(cursor!=end) throw new InvalidDataException("Visibility spatial section end mismatch.");
        var changes=new Dictionary<int,Box>(); var ids=new HashSet<int>(); int expanded=0;
        var remapped=new Dictionary<int,int>();
        foreach(var node in nodes)
        foreach(int record in node.Records)
        {
            int type=I(record+12);
            if(mappings is not null && mappings.TryGetValue(type,out var mapping)) {
                if(!mapping.TryGetValue(I(record+28),out int target)) throw new InvalidDataException("Unmapped visibility placement ID.");
                remapped.Add(record+28,target);
            }
            if(type!=0) continue;
            int id=I(record+28);
            if(id<0 || id>=before.Length || !ids.Add(id) || !Read(record).Near(before[id])) throw new InvalidDataException("Visibility tile ID/bounds do not match source geometry.");
            var updated=Read(record).Union(after[id]); changes[record]=updated;
            if(Read(record).Contains(updated)) continue;
            expanded++;
            // Updating every enclosing original AABB also updates ancestors,
            // without depending on undocumented spatial-tree link packing.
            foreach(var ancestor in nodes.Where(n=>n.Bounds.Contains(node.Bounds)))
                changes[ancestor.Offset]=changes.GetValueOrDefault(ancestor.Offset,ancestor.Bounds).Union(updated);
        }
        if(ids.Count!=before.Length) throw new InvalidDataException("Missing visibility terrain records.");
        foreach(var pair in remapped) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(pair.Key,4),pair.Value);
        foreach(var pair in changes)
            for(int k=0;k<3;k++) {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(pair.Key+k*4,4),pair.Value.Min[k]);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(pair.Key+16+k*4,4),pair.Value.Max[k]);
            }
        _=VisibilityAudit.AllVisible(bytes);
        return new { TerrainRecords=ids.Count, ExpandedTiles=expanded, UpdatedSpatialNodes=changes.Keys.Count(p=>nodes.Any(n=>n.Offset==p)),
            PlacementRecordsRemapped=remapped.Count, TerrainIdsAndTreeLinksPreserved=true, ConservativePvs=true, FrustumCullingRetained=true, RuntimeValidated=false };
    }
}
