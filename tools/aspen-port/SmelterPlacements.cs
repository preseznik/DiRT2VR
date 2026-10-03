using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

internal static class SmelterPlacements
{
    internal static byte[] Dry(byte[] source, XDocument entities, out object report)
    {
        int I(int at) {
            if(at<0 || (long)at+4>source.Length) throw new InvalidDataException("Truncated dependent placement.");
            return BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(at,4));
        }
        string S(int at) {
            if(at<0 || at>=source.Length) throw new InvalidDataException("Invalid dependent string.");
            int end=Array.IndexOf(source,(byte)0,at);
            if(end<at || end-at>512) throw new InvalidDataException("Invalid dependent string.");
            return Encoding.ASCII.GetString(source,at,end-at);
        }
        int list=I(4), dep=I(12), count=I(dep+12), instances=I(dep+20);
        if(I(16)!=1 || count<0 || count>10000 || instances<0 || instances>100000 || I(dep)!=count || I(dep+4)!=instances)
            throw new InvalidDataException("Unexpected Smelter dependent list.");
        var references=new Dictionary<int,(string Name,int Count)>();
        for(int i=0;i<count;i++) {
            int p=checked(I(dep+8)+i*44);string name=S(I(p+4)),type=S(I(p+8));
            if(type!="wet2" || !name.EndsWith("_wet2",StringComparison.Ordinal) || !references.TryAdd(I(p),(name,I(p+40))))
                throw new InvalidDataException("Dry Smelter candidate cannot omit a non-wet dependent model: "+name);
        }
        var parents=new HashSet<int>(entities.Descendants("TEMPLATEENTITYINSTANCE").Select(e=>(int)e.Attribute("instanceID")!));
        for(int i=0;i<I(list+48);i++) parents.Add(I(checked(I(list+44)+i*88+4)));
        var seen=new HashSet<int>();var counts=references.Keys.ToDictionary(k=>k,k=>0);
        for(int i=0;i<instances;i++) {
            int p=checked(I(dep+16)+i*12);
            if(!seen.Add(I(p)) || !counts.ContainsKey(I(p+4)) || !parents.Contains(I(p+8)))
                throw new InvalidDataException("Unresolved wet-only placement dependency.");
            counts[I(p+4)]++;
        }
        if(references.Any(r=>r.Value.Count!=counts[r.Key])) throw new InvalidDataException("Wet-only instance count mismatch.");
        var result=(byte[])source.Clone();
        foreach(int offset in new[]{0,4,12,20}) BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(dep+offset,4),0);
        report=new { Condition="Dry morning",WetOnlyModels=references.Values.Select(r=>r.Name).Order().ToArray(),WetOnlyInstances=instances,ParentInstancesPreserved=true,Reason="Source wet2 overlays are inactive in the selected dry condition; wet layouts require separate conversion." };
        return result;
    }
}
