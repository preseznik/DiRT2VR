using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

// Aspen's stationary crowd layout is shared with DiRT 2 except for the
// trailing day-only flag in each 44-byte placement (DiRT 2 uses 40 bytes).
internal static class Crowds
{
    static readonly Dictionary<string,string> Aliases = new(StringComparer.Ordinal)
    {
        ["m_static_var_A"]="m_cr", ["m_static_var_B"]="m_cr",
        ["m_static_var_C"]="m_cr", ["m_static_var_E"]="m_cr",
        ["m_static_var_G"]="m_cr", ["m_static_var_H"]="m_cr",
        // Keep fallback identities for isolated role tests. Normal conversion
        // explicitly restores the verified camera roles with StaffResources;
        // seated and flare roles retain these working race substitutes.
        ["pre-race_photographer_01"]="m_cr", ["m_cameraman_01"]="m_cr",
        ["m_flare_01"]="m_cr", ["m_seated"]="m_gr", ["f_seated"]="f_gr"
    };

    internal static byte[] Convert(byte[] source, XDocument definitions, List<object> report,IReadOnlySet<string>? restoredRoles=null)
    {
        void Range(int at,int count) {
            if(at<0 || count<0 || (long)at+count>source.Length) throw new InvalidDataException("Crowd range outside source.");
        }
        int I(int at) { Range(at,4);return BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(at)); }
        string S(int at) {
            Range(at,1);int end=Array.IndexOf(source,(byte)0,at);
            if(end<at || end-at>128)throw new InvalidDataException("Invalid crowd reference name.");
            return Encoding.ASCII.GetString(source,at,end-at);
        }
        if(I(0)!=12 || I(4)!=36)throw new InvalidDataException("Unsupported crowd header.");
        int header=I(8), blocks=I(36), blockTable=I(40);
        if(blocks<1 || blocks>100000 || I(44)!=blocks || blockTable!=48 || header!=checked(48+blocks*40))
            throw new InvalidDataException("Invalid crowd visibility table.");
        Range(blockTable,checked(blocks*40));Range(header,24);
        int references=I(header),total=I(header+4),table=I(header+16);
        if(references<1 || references>4096 || total<1 || total>1000000 || I(header+12)!=0 || I(header+8)!=table || I(header+20)!=references || table!=header+24)
            throw new InvalidDataException("Unsupported moving crowd or reference table.");
        Range(table,checked(references*16));
        var known=definitions.Descendants("instanceSet").Select(n=>(string?)n.Attribute("name")).ToHashSet(StringComparer.Ordinal);
        var rows=new List<(int Source,int Count,string Name)>();
        int next=checked(table+references*16),sum=0;
        var blockCounts=new int[blocks];
        for(int n=0;n<references;n++) {
            int at=table+n*16,count=I(at+4),data=I(at+8);
            string name=S(I(at)),target=restoredRoles?.Contains(name)==true ? (name=="pre-race_photographer_01"?"m_photo_01":name) : Aliases.GetValueOrDefault(name,name);
            if(!known.Contains(target))throw new InvalidDataException("Unsupported spectator type: "+name);
            if(count<1 || count>total || I(at+12)!=count || data!=next)throw new InvalidDataException("Invalid crowd placement span.");
            Range(data,checked(count*44));next=checked(next+count*44);sum=checked(sum+count);
            for(int i=0;i<count;i++) {
                int record=data+i*44,block=I(record+36);
                if(block<0 || block>=blocks || I(record+40)!=0)throw new InvalidDataException("Unsupported crowd block or day-only flag.");
                for(int k=0;k<9;k++)if(!float.IsFinite(BitConverter.ToSingle(source,record+k*4)))throw new InvalidDataException("Nonfinite crowd placement.");
                blockCounts[block]++;
            }
            rows.Add((data,count,target));
            report.Add(new { SourceType=name,TargetType=target,Instances=count,SourceStride=44,TargetStride=40,PlacementBytesPreserved=true });
        }
        if(sum!=total)throw new InvalidDataException("Crowd total disagrees with references.");
        for(int n=0;n<blocks;n++) {
            int at=blockTable+n*40;
            if(I(at)!=n || I(at+8)!=blockCounts[n] || I(at+12)!=0)throw new InvalidDataException("Crowd visibility counts disagree with placements.");
        }
        for(int n=0;n<references;n++)if(I(table+n*16)<next)throw new InvalidDataException("Crowd string overlaps placement data.");
        // DiRT 2 resolves a single record per reference name. Aliases can merge
        // multiple DiRT 3 types; emit one contiguous group for each target.
        var groups=rows.GroupBy(r=>r.Name,StringComparer.Ordinal).ToArray();
        using var output=new MemoryStream();using var writer=new BinaryWriter(output,Encoding.ASCII,true);
        writer.Write(source,0,table);writer.Write(new byte[groups.Length*16]);
        void Put(int at,int value) { long saved=output.Position;output.Position=at;writer.Write(value);output.Position=saved; }
        Put(header,groups.Length);Put(header+20,groups.Length);
        for(int n=0;n<groups.Length;n++) {
            int count=groups[n].Sum(r=>r.Count);
            Put(table+n*16+4,count);Put(table+n*16+12,count);Put(table+n*16+8,checked((int)output.Position));
            foreach(var row in groups[n])for(int i=0;i<row.Count;i++)writer.Write(source,row.Source+i*44,40);
        }
        for(int n=0;n<groups.Length;n++) {
            Put(table+n*16,checked((int)output.Position));writer.Write(Encoding.ASCII.GetBytes(groups[n].Key));writer.Write((byte)0);
        }
        return output.ToArray();
    }

    internal static object Restore(string track,string source,string game,Dictionary<string,string>? fingerprints=null,IReadOnlySet<string>? restoredRoles=null,string sourceRoute="route_0")
    {
        var input=Path.Combine(source,sourceRoute,"crowd_standing2.bin");
        var definitions=Path.Combine(game,"anims/crowdDefs.xml");
        PortFiles.NoLinks(input);PortFiles.NoLinks(definitions);
        var report=new List<object>();
        var bytes=Convert(File.ReadAllBytes(input),PortFiles.ReadXml(definitions),report,restoredRoles);
        var target=Path.Combine(track,"route_0/crowd_standing2.bin");
        var datasetPath=Path.Combine(track,"route_0/organism_track_dataset.xml");
        var dataset=PortFiles.ReadXml(datasetPath);
        var dependencies=new Dictionary<string,string> { [input]=PortFiles.Hash(input),[definitions]=PortFiles.Hash(definitions) };
        foreach(var entry in dataset.Descendants("pssg")) {
            string name=(string?)entry.Attribute("filename") ?? "";
            if(!name.StartsWith("/data/anims/",StringComparison.Ordinal) || name.Contains("..") || name.Contains('\\'))throw new InvalidDataException("Unexpected crowd dependency path.");
            var file=Path.Combine(game,name[6..]);dependencies[file]=PortFiles.Hash(file);
        }
        if(fingerprints is not null)foreach(var entry in dependencies)fingerprints[entry.Key]=entry.Value;
        PortFiles.NoLinks(target);
        using(var output=new FileStream(target+".tmp",FileMode.CreateNew))output.Write(bytes);
        File.Move(target+".tmp",target,true);
        return new { Instances=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8))+4)),
            Types=report,Dependencies=dependencies,UsesDirt2Characters=true,RuntimeValidated=false };
    }
}
