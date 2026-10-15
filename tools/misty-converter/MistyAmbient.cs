using System.IO.Compression;
using System.Numerics;
using System.Text;
using DiRT2VR.Nordschleife;

// The pinned track uses CSP's original Patch.data format: name, type,
// first vertex, vertex count, then RGB (type 0) or scalar (type 1) half floats.
// Match before compaction, batching or tiling changes the vertex order.
internal sealed class MistyAmbient
{
    sealed record Entry(string Name, Vector3 First, Vector3[] Values);
    readonly Dictionary<string,Entry[]> entries;
    readonly HashSet<Entry> used=[];
    readonly List<object> applied=[];
    readonly float opacity,brightness;
    internal MistyAmbient(string path)
    {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count!=4 || zip.Entries.Select(e=>e.FullName).Distinct().Count()!=4)
            throw new InvalidDataException("Unsupported Misty ambient archive.");
        var patch=zip.GetEntry("Patch.data")??throw new InvalidDataException("Missing Misty ambient data.");
        var config=zip.GetEntry("Config.ini")??throw new InvalidDataException("Missing Misty ambient settings.");
        if(patch.Length is <24 or >32*1024*1024 || config.Length>4096)throw new InvalidDataException("Misty ambient data exceeds limits.");
        using(var reader=new StreamReader(config.Open()))
        {
            var values=Scene.Ini(reader.ReadToEnd())["LIGHTING"];
            float Value(string name)=>float.Parse(values[name],System.Globalization.CultureInfo.InvariantCulture);
            opacity=Value("OPACITY");brightness=Value("BRIGHTNESS");
            if(opacity!=.8f||brightness!=1.1f||values.Count!=2)throw new InvalidDataException("Misty ambient settings changed.");
        }
        using var data=new MemoryStream();using(var input=patch.Open())input.CopyTo(data);
        if(data.Length!=patch.Length)throw new InvalidDataException("Truncated Misty ambient data.");
        data.Position=0;using var r=new BinaryReader(data,new UTF8Encoding(false,true));var list=new List<Entry>();
        while(data.Position<data.Length)
        {
            int length=r.ReadInt32();if(length is <1 or >1024)throw new InvalidDataException("Invalid ambient mesh name.");
            string name=new UTF8Encoding(false,true).GetString(r.ReadBytes(length));int type=r.ReadInt32();
            var first=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());int count=r.ReadInt32();
            if(type is not (0 or 1)||!Kn5.Finite(first)||count is <1 or >65535||list.Count>=10000||data.Length-data.Position<count*(type==0?6L:2L))
                throw new InvalidDataException("Invalid ambient mesh record: "+name);
            float Half(){float v=(float)BitConverter.UInt16BitsToHalf(r.ReadUInt16());if(!float.IsFinite(v)||v is <0 or >1)throw new InvalidDataException("Invalid ambient sample.");return v;}
            var samples=new Vector3[count];
            for(int i=0;i<count;i++)samples[i]=type==0?new(Half(),Half(),Half()):new(Half());
            list.Add(new(name,first,samples));
        }
        entries=list.GroupBy(e=>e.Name,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
    }
    internal Mesh Apply(Mesh mesh)
    {
        // Animated water/boats retain their accepted native materials and motion.
        bool supported=Visuals.Target(mesh.Material) is "misty_object_rgb.fx" or "terrain_misty_ground.fx";
        var values=Enumerable.Repeat(Vector3.One,mesh.Positions.Length).ToArray();
        if(supported&&entries.TryGetValue(mesh.Name,out var candidates))
        {
            var matches=candidates.Where(e=>e.Values.Length==mesh.Positions.Length&&Vector3.Distance(e.First,mesh.SourceFirstPosition)<.002f).ToArray();
            if(matches.Length!=1)throw new InvalidDataException("Source ambient data does not match mesh vertices: "+mesh.Name);
            var entry=matches[0];if(!used.Add(entry))throw new InvalidDataException("Ambiguous ambient mesh: "+mesh.Name);
            values=entry.Values.Select(v=>Vector3.Clamp((Vector3.One*(1-opacity)+v*opacity)*brightness,Vector3.Zero,Vector3.One)).ToArray();
            applied.Add(new{mesh.Name,mesh.Material.Id,Vertices=values.Length,Minimum=values.Min(v=>Math.Min(v.X,Math.Min(v.Y,v.Z))),Maximum=values.Max(v=>Math.Max(v.X,Math.Max(v.Y,v.Z)))});
        }
        return supported?mesh with{Colors=values.Select(v=>new Vector4(v,1)).ToArray()}:mesh;
    }
    internal void WriteAudit(string output)
    {
        if(used.Count<100)throw new InvalidDataException("Too little source ambient shading matched; refusing an incomplete HQ build.");
        Files.Json(Path.Combine(output,"ambient-audit.json"),new{Source="rt_misty_loch.vao-patch",Records=entries.Values.Sum(e=>e.Length),Matched=used.Count,
            Opacity=opacity,Brightness=brightness,AmbientOnly=true,OriginalVertexOrder=true,Meshes=applied,
            Unused=entries.Values.SelectMany(e=>e).Except(used).Select(e=>e.Name).ToArray()});
    }
}
