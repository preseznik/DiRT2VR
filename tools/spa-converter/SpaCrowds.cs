using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

// GRID 2 stores 48-byte actors; DiRT 2's runtime requires 40-byte actors.
// Preserve authored position/yaw and use loaded native race characters/parameters.
internal static class SpaCrowds
{
    static int I(byte[] b,int p)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p,4));
    static float F(byte[] b,int p)=>BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(p,4));
    static string S(byte[] b,int p)=>Encoding.ASCII.GetString(b,p,Array.IndexOf(b,(byte)0,p)-p);
    sealed record Row(string Name,int Start,int Count);
    static Row[] Rows(byte[] b,int stride,bool source=true)
    {
        int h=I(b,8),table=I(b,h+16),count=I(b,h);
        if(I(b,0)!=12||I(b,4)!=36||I(b,40)!=48||I(b,36)!=I(b,44)||h!=48+I(b,36)*40||(source&&(table!=h+24||count!=I(b,h+20)||I(b,h+12)!=0)))throw new InvalidDataException("Crowd header");
        count=I(b,h+20);
        var rows=Enumerable.Range(0,count).Select(n=>new Row(S(b,I(b,table+n*16)),I(b,table+n*16+8),I(b,table+n*16+4))).ToArray();
        if(!source)return rows;
        int next=table+count*16;
        foreach(var r in rows){if(r.Start!=next||r.Count<=0)throw new InvalidDataException("Crowd spans");next=checked(next+r.Count*stride);}
        if(rows.Sum(r=>r.Count)!=I(b,h+4)||Enumerable.Range(0,count).Any(n=>I(b,table+n*16)<next))throw new InvalidDataException("Crowd inventory");
        return rows;
    }
    static string Role(string name)=>name switch {
        "f_cr" or "f_cr_le" or "f_anim_static_01"=>"f_cr",
        "m_cr" or "m_anim_static_01" or "m_cameraman_01" or "m_seated" or "pre-race_photographer_01"=>"m_cr",
        _ when name.StartsWith("m_static_var_",StringComparison.Ordinal)=>"m_cr",
        _ when name.StartsWith("pre-race_marshal_",StringComparison.Ordinal)=>"ma_np",
        _ when name.StartsWith("gr_fin_",StringComparison.Ordinal)=>int.Parse(name[7..])%2==0?"f_cr":"m_cr",
        _=>throw new InvalidDataException("Unknown source crowd role: "+name)
    };
    internal static void Write(string source,string game,string output,Vector3 origin)
    {
        string input=Files.Inside(source,"tracks/locations/europe/spa_francorchamps/route_0/crowd_standing2.bin");
        string donorPath=Files.Inside(game,"tracks/london/battersea/route_1/crowd_standing2.bin");
        byte[] b=File.ReadAllBytes(input),native=File.ReadAllBytes(donorPath);var rows=Rows(b,48);var templates=Rows(native,40,false).ToDictionary(r=>r.Name);
        if(rows.Length!=33||rows.Sum(r=>r.Count)!=20181)throw new InvalidDataException("Unexpected Spa crowd");
        int blocks=I(b,36);var counts=new int[blocks];
        var grid=Enumerable.Range(0,blocks).ToDictionary(n=>I(b,48+n*40+4),n=>n);
        var groups=new Dictionary<string,List<byte[]>>();var report=new List<object>();
        foreach(var row in rows){
            string role=Role(row.Name);var template=templates[role];
            if(!groups.TryGetValue(role,out var records))groups.Add(role,records=[]);
            for(int n=0;n<row.Count;n++){
                int at=row.Start+n*48;for(int j=0;j<5;j++)if(!float.IsFinite(F(b,at+j*4)))throw new InvalidDataException("Nonfinite source actor");
                int x=(int)MathF.Floor((F(b,at+8)-F(b,12))/F(b,20)),z=(int)MathF.Floor((F(b,at+16)-F(b,16))/F(b,24));
                if(x<0||z<0||x>=I(b,28)||z>=I(b,32)||!grid.TryGetValue(x+z*I(b,28),out int block))throw new InvalidDataException("Actor outside source visibility grid");
                counts[block]++;byte[] r=new byte[40];b.AsSpan(at,20).CopyTo(r);
                for(int k=0;k<3;k++)BinaryPrimitives.WriteSingleLittleEndian(r.AsSpan(8+k*4,4),F(b,at+8+k*4)-origin[k]);
                native.AsSpan(template.Start+n%template.Count*40+20,16).CopyTo(r.AsSpan(20,16));
                BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(36,4),block);records.Add(r);
            }
            report.Add(new{Source=row.Name,Native=role,row.Count});
        }
        for(int n=0;n<blocks;n++)if(counts[n]!=I(b,48+n*40+8))throw new InvalidDataException("Recovered source crowd blocks disagree");
        byte[] header=b.AsSpan(0,I(b,8)+24).ToArray();
        void Shift(int p,float value)=>BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(p,4),F(header,p)-value);
        Shift(12,origin.X);Shift(16,origin.Z);
        for(int n=0;n<blocks;n++)for(int bound=0;bound<2;bound++)for(int k=0;k<3;k++)Shift(48+n*40+16+bound*12+k*4,origin[k]);
        byte[] Encode(int stride){
            using var memory=new MemoryStream();using var w=new BinaryWriter(memory);w.Write(header);w.Write(new byte[groups.Count*16]);
            void Put(int p,int value){long end=memory.Position;memory.Position=p;w.Write(value);memory.Position=end;}
            int h=I(header,8),table=h+24;Put(h,groups.Count);Put(h+20,groups.Count);int index=0;
            foreach(var (role,records) in groups){int row=table+index++*16;Put(row+4,records.Count);Put(row+12,records.Count);Put(row+8,(int)memory.Position);foreach(var r in records){w.Write(r,0,stride==40?40:24);if(stride==28)w.Write(r,36,4);}}
            index=0;foreach(string role in groups.Keys){Put(table+index++*16,(int)memory.Position);w.Write(Encoding.ASCII.GetBytes(role));w.Write((byte)0);}
            return memory.ToArray();
        }
        foreach(int stride in new[]{40,28}){
            var encoded=Encode(stride);var saved=Rows(encoded,stride);int index=0;
            foreach(var records in groups.Values){var row=saved[index++];if(row.Count!=records.Count)throw new InvalidDataException("Crowd round trip count");for(int n=0;n<row.Count;n++){var r=encoded.AsSpan(row.Start+n*stride,stride);if(!r[..20].SequenceEqual(records[n].AsSpan(0,20))||I(encoded,row.Start+n*stride+stride-4)!=I(records[n],36))throw new InvalidDataException("Crowd round trip placement");}}
            File.WriteAllBytes(Path.Combine(output,stride==40?"crowd_standing2.bin":"crowd_standing.bin"),encoded);
        }
        string dataset=Files.Inside(game,"tracks/london/battersea/route_1/organism_track_dataset.xml");
        using var dsInput=File.OpenRead(dataset);var ds=XDocument.Parse(new XmlFile(dsInput).Document.OuterXml);
        var dependencies=new Dictionary<string,string>{{input,Files.Hash(input)},{donorPath,Files.Hash(donorPath)},{dataset,Files.Hash(dataset)}};
        foreach(var p in ds.Descendants("pssg")){string path=Files.Inside(game,((string)p.Attribute("filename")!)[6..]);dependencies.Add(path,Files.Hash(path));}
        File.Copy(dataset,Path.Combine(output,"organism_track_dataset.xml"));
        Files.Json(Path.Combine(output,"crowds.json"),new{Passed=true,Actors=20181,VisibilityBlocks=blocks,SourceStride=48,NativeStride=40,LegacyStride=28,SourcePositionsAndYawPreserved=true,SourceVisibilityMembershipChecked=true,SourceCharactersReplacedByNativeDirt2RaceCharacters=true,SourceAnimationAppearanceAndStaffPropsApproximate=true,Roles=report,Dependencies=dependencies,RuntimeValidated=false});
    }
}
