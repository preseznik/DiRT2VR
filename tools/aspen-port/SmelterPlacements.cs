using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

internal static class SmelterPlacements
{
    internal static byte[] Dry(byte[] source, XDocument entities, out object report)
        => Prepare(source, entities, false, out report);
    internal static byte[] Prepare(byte[] source, XDocument entities, bool wet, out object report)
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
        if (!wet) return result;
        // Promote wet dependencies to ordinary static instances with their
        // parent's exact transform. Dynamic parents would need runtime links.
        var staticParents=Enumerable.Range(0,I(list+48)).Select(i=>I(list+44)+i*88).ToDictionary(p=>I(p+4));
        int refCount=I(list+40), oldSlots=I(list+28), firstRef=Enumerable.Range(0,refCount).Max(i=>I(I(list+36)+i*48+4))+1;
        using var output=new MemoryStream(); using var writer=new BinaryWriter(output);
        writer.Write(result); while(output.Position%4!=0) writer.Write((byte)0);
        int newRefs=checked((int)output.Position);
        writer.Write(source,I(list+36),checked(refCount*48));
        var mapped=new Dictionary<int,int>();
        for(int i=0;i<count;i++) {
            int p=I(dep+8)+i*44; mapped.Add(I(p),firstRef+i);
            writer.Write(I(p+4)); writer.Write(firstRef+i);
            writer.Write(source,p+16,24); // dependent bounds
            writer.Write(0); writer.Write(I(p+12)); writer.Write(I(p+40)); writer.Write(-1);
        }
        int newInstances=checked((int)output.Position);
        writer.Write(source,I(list+44),checked(I(list+48)*88));
        for(int i=0;i<instances;i++) {
            int p=I(dep+16)+i*12;
            if(!staticParents.TryGetValue(I(p+8),out int parent) || I(parent+64)!=0)
                throw new InvalidDataException("Wet overlay has a movable parent; runtime linkage is required.");
            var instance=source.AsSpan(parent,88).ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(instance,mapped[I(p+4)]);
            BinaryPrimitives.WriteInt32LittleEndian(instance.AsSpan(4),oldSlots+i);
            writer.Write(instance);
        }
        result=output.ToArray();
        void Put(int p,int v)=>BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(p,4),v);
        Put(list+24,refCount+count);Put(list+28,oldSlots+instances);
        Put(list+36,newRefs);Put(list+40,refCount+count);
        Put(list+44,newInstances);Put(list+48,I(list+48)+instances);
        report=new {Condition="Wet",WetOnlyModels=references.Values.Select(r=>r.Name).Order().ToArray(),WetOnlyInstances=instances,
            ParentInstancesPreserved=true,PromotedStaticOverlays=instances,RuntimeValidated=false};
        return result;
    }
}
