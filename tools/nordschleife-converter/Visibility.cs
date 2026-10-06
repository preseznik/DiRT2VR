using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace DiRT2VR.Nordschleife;

// Bounded first-gate adaptation. Reuses the inspected donor topology, including unused placement slots.
// A full-course visibility compiler is a later gate, after this segment's driving acceptance.
internal static class Visibility
{
    // Format probe only: a single view leaf and flat spatial list, with no borrowed
    // terrain IDs. All-visible PVS is deliberately unsuitable for final-course culling.
    internal static void WriteDiagnostic(string source,string output,Bounds[] tiles)
    {
        if(tiles.Length is <1 or >65535 || tiles.Any(b=>!Kn5.Finite(b.Min)||!Kn5.Finite(b.Max)||Enumerable.Range(0,3).Any(k=>b.Min[k]>b.Max[k])))
            throw new InvalidDataException("Invalid visibility tile inventory.");
        var donor=File.ReadAllBytes(source);
        if(Convert.ToHexString(SHA256.HashData(donor))!="B5A4CE10FDD433DF71814EBF081581ED6CED3966E34E01DB645809A7F1DEAA97")throw new InvalidDataException("Unsupported donor visibility fingerprint.");
        const int view=128,mask=144;
        // Leave ample space for the donor's hidden non-terrain inventories. The raw
        // mask has no zero bits; its section size is explicit in the file header.
        int maskBytes=checked(BinaryPrimitives.ReadInt32LittleEndian(donor.AsSpan(16,4))+((tiles.Length+127)/128)*16+128);
        int spatial=mask+maskBytes;
        var bytes=new byte[checked(spatial+48+tiles.Length*32)];donor.AsSpan(0,128).CopyTo(bytes);
        void I(int p,int v)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(p,4),v);
        void Box(int p,Bounds b)
        {
            for(int k=0;k<3;k++) {BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p+k*4,4),b.Min[k]);BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p+16+k*4,4),b.Max[k]);}
        }
        I(4,1);I(8,1);I(12,1);I(16,maskBytes);I(20,view);I(24,mask);I(28,spatial);I(64,tiles.Length);
        var bounds=tiles.Aggregate((a,b)=>a.Union(b));Box(32,bounds);Box(spatial,bounds);
        bytes[view+2]=(byte)mask;bytes[view+3]=(byte)(mask>>8);bytes[view+4]=(byte)(mask>>16);
        bytes.AsSpan(mask,maskBytes).Fill(255);
        I(spatial+28,tiles.Length<<16);
        for(int i=0;i<tiles.Length;i++) {int p=spatial+48+i*32;Box(p,tiles[i]);I(p+28,i);}
        File.WriteAllBytes(Path.Combine(output,"track.vis"),bytes);
        Files.Json(Path.Combine(output,"visibility.json"),new{TerrainTiles=tiles.Length,SpatialNodes=1,Leaves=1,ConservativeRegionMasks=true,SourceTopologyPreserved=false,FullCourseCompiler=false,DiagnosticFlatSpatialList=true,RuntimeValidated=false});
    }

    internal static void Write(string source,string output,Bounds[] before,Bounds[] after)
    {
        var bytes=File.ReadAllBytes(source);var original=bytes.ToArray();
        if(Convert.ToHexString(SHA256.HashData(bytes))!="B5A4CE10FDD433DF71814EBF081581ED6CED3966E34E01DB645809A7F1DEAA97")throw new InvalidDataException("Unsupported donor visibility fingerprint.");
        int I(int p)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p,4));
        Bounds Read(int p)=>new(new(BitConverter.ToSingle(bytes,p),BitConverter.ToSingle(bytes,p+4),BitConverter.ToSingle(bytes,p+8)),new(BitConverter.ToSingle(bytes,p+16),BitConverter.ToSingle(bytes,p+20),BitConverter.ToSingle(bytes,p+24)));
        var owned=new HashSet<int>();
        void Set(int p,Bounds b)
        {
            for(int k=0;k<3;k++) {BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p+k*4,4),b.Min[k]);BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p+16+k*4,4),b.Max[k]);}
            foreach(int start in new[]{p,p+16})for(int k=0;k<12;k++)owned.Add(start+k);
        }
        if(bytes.Length!=151008 || I(0)!=4 || I(12)!=599 || I(64)!=before.Length || before.Length!=after.Length)throw new InvalidDataException("Unsupported VIS tile inventory.");
        var nodes=new List<(int Offset,Bounds Bounds,int[] Records)>();int cursor=I(28);
        for(int i=0;i<I(12);i++)
        {
            if(cursor+48>bytes.Length || (I(cursor+28)&65535)!=i)throw new InvalidDataException("VIS node sequence mismatch.");
            int count=(int)((uint)I(cursor+28)>>16),next=checked(cursor+48+count*32);
            if(next>bytes.Length || I(cursor+32)!=(i==I(12)-1?0:next))throw new InvalidDataException("VIS record range mismatch.");
            nodes.Add((cursor,Read(cursor),Enumerable.Range(0,count).Select(k=>cursor+48+k*32).ToArray()));cursor=next;
        }
        if(cursor!=bytes.Length)throw new InvalidDataException("Unexpected VIS tail.");
        var ids=new HashSet<int>();var changes=new Dictionary<int,Bounds>();
        foreach(var node in nodes)foreach(int record in node.Records)
        {
            if(I(record+12)!=0)continue;int id=I(record+28);
            if(id<0 || id>=before.Length || !ids.Add(id) || !Read(record).Contains(before[id]) || !before[id].Contains(Read(record)))throw new InvalidDataException("VIS/terrain tile identity mismatch.");
            var expanded=Read(record).Union(after[id]);changes[record]=expanded;
            foreach(var parent in nodes.Where(n=>n.Bounds.Contains(node.Bounds)))changes[parent.Offset]=changes.GetValueOrDefault(parent.Offset,parent.Bounds).Union(expanded);
        }
        if(ids.Count!=before.Length)throw new InvalidDataException("Missing terrain visibility records.");
        foreach(var change in changes)Set(change.Key,change.Value);
        // Also expand the PVS view-tree envelope; moving outside the donor world must not escape it.
        Set(32,Read(32).Union(after.Aggregate((a,b)=>a.Union(b))));
        int maskStart=I(24),maskBytes=I(16),leafCount=0;
        if(maskStart>0xffffff || maskStart+maskBytes>I(28))throw new InvalidDataException("No room for raw visibility mask.");
        for(int i=0;i<I(4);i++)
        {
            int p=I(20)+i*6;if(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p,2))!=0)continue;
            leafCount++;bytes[p+2]=(byte)maskStart;bytes[p+3]=(byte)(maskStart>>8);bytes[p+4]=(byte)(maskStart>>16);bytes[p+5]=0;
            for(int k=2;k<6;k++)owned.Add(p+k);
        }
        if(leafCount!=I(8))throw new InvalidDataException("VIS leaf count mismatch.");
        bytes.AsSpan(maskStart,maskBytes).Fill(255);for(int i=0;i<maskBytes;i++)owned.Add(maskStart+i);
        if(Enumerable.Range(0,bytes.Length).Any(i=>bytes[i]!=original[i]&&!owned.Contains(i)))throw new InvalidDataException("Unexpected VIS modification.");
        File.WriteAllBytes(Path.Combine(output,"track.vis"),bytes);
        Files.Json(Path.Combine(output,"visibility.json"),new{TerrainTiles=ids.Count,SpatialNodes=nodes.Count,Leaves=leafCount,ConservativeRegionMasks=true,FrustumCullingRetained=true,SourceTopologyPreserved=true,FullCourseCompiler=false,RuntimeValidated=false});
    }
}
