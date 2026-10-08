using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

// A bounded native AO experiment. GRID 2's 3D ambient SH data is not a DiRT 2
// scalar CLM. Project actual roof coverage near the driving line instead; keep
// full ambient light outside covered sections. HDR exposure stays untouched.
internal static class MizuVehicleOcclusion
{
    const int Resolution=2000, Radix=10, NodeBytes=104;
    sealed record Roof(Vector3 A,Vector3 B,Vector3 C);
    sealed record Road(Vector3 A,Vector3 B);
    static Cell CellAt(Vector3 p)=>new((int)MathF.Floor(p.X/64),(int)MathF.Floor(p.Z/64));
    static void Index<T>(Dictionary<Cell,List<T>> index,T value,Vector3 min,Vector3 max)
    {
        var a=CellAt(min);var b=CellAt(max);
        for(int x=a.X;x<=b.X;x++)for(int z=a.Z;z<=b.Z;z++)
        {
            var cell=new Cell(x,z);if(!index.TryGetValue(cell,out var items))index[cell]=items=[];items.Add(value);
        }
    }
    static bool Covered(Vector3 p,Dictionary<Cell,List<Roof>> roofs)
    {
        if(!roofs.TryGetValue(CellAt(p),out var items))return false;
        foreach(var t in items)
        {
            var ab=t.B-t.A;var ac=t.C-t.A;var ap=p-t.A;
            float det=ab.X*ac.Z-ac.X*ab.Z;
            if(MathF.Abs(det)<1e-5f)continue;
            float u=(ap.X*ac.Z-ac.X*ap.Z)/det,v=(ab.X*ap.Z-ap.X*ab.Z)/det;
            if(u<-.0001f||v<-.0001f||u+v>1.0001f)continue;
            float clearance=t.A.Y+u*ab.Y+v*ac.Y-p.Y;
            if(clearance is >=3 and <=18)return true;
        }
        return false;
    }
    internal static void Write(string grid2,string output)=>Build(grid2,output,output,false);
    static void Build(string grid2,string candidate,string output,bool probe)
    {
        var inputs=new Dictionary<string,string>();
        string Input(string name){Files.NoLinks(name);inputs.Add(name,Files.Hash(name));return name;}
        string venue=Files.Inside(grid2,"tracks/locations/p2p/okutama");
        using var segment=JsonDocument.Parse(File.ReadAllText(Input(Files.Inside(candidate,"segment.json"))));
        var o=segment.RootElement.GetProperty("TranslationOnly");var origin=new Vector3(o[0].GetSingle(),o[1].GetSingle(),o[2].GetSingle());
        if(!segment.RootElement.GetProperty("FullCourse").GetBoolean())throw new InvalidDataException("Vehicle AO probe requires the full route");
        using var progressInput=File.OpenRead(Input(Files.Inside(venue,"route_0/progress_track.xml")));
        var progress=XDocument.Parse(new XmlFile(progressInput).Document.OuterXml);
        static Vector3 V(string s){var f=s.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();return new(f[0],f[1],f[2]);}
        int start=(int)progress.Descendants("split").Single(s=>(string?)s.Attribute("type")=="start").Attribute("gate")!;
        int finish=(int)progress.Descendants("split").Single(s=>(string?)s.Attribute("type")=="finish").Attribute("gate")!;
        var gates=progress.Descendants("gate").Select(g=>(V(g.Element("left")!.Value)+V(g.Element("right")!.Value))/2-origin).Skip(start).Take(finish-start+1).ToArray();
        var roads=new Dictionary<Cell,List<Road>>();
        for(int i=1;i<gates.Length;i++)Index(roads,new Road(gates[i-1],gates[i]),Vector3.Min(gates[i-1],gates[i])-new Vector3(25),Vector3.Max(gates[i-1],gates[i])+new Vector3(25));
        var source=Files.Pssg(Input(Files.Inside(venue,"tracksplit.pssg")));var roofs=new Dictionary<Cell,List<Roof>>();int triangles=0;
        foreach(var node in source.Elements<PssgRenderNode>().Where(n=>n.Id.StartsWith("SHADOWCASTING_",StringComparison.Ordinal)))
        {
            if(node.Transform.Transform!=Matrix4x4.Identity)throw new InvalidDataException("Unexpected caster transform");
            foreach(var draw in node.ChildElements.OfType<PssgRenderStreamInstance>())
            {
                if(!draw.GetShaderInstance().GetShaderGroup().Id.StartsWith("terrain_",StringComparison.Ordinal))continue;
                var r=new RenderDataSourceReader(draw.GetRenderDataSource());
                for(int i=0;i<r.IndexCount;i+=3)
                {
                    var a=r.GetPosition(r.GetIndex(i))-origin;var b=r.GetPosition(r.GetIndex(i+1))-origin;var c=r.GetPosition(r.GetIndex(i+2))-origin;
                    if(MathF.Abs((b.X-a.X)*(c.Z-a.Z)-(c.X-a.X)*(b.Z-a.Z))<1e-5f)continue;
                    Index(roofs,new Roof(a,b,c),Vector3.Min(a,Vector3.Min(b,c)),Vector3.Max(a,Vector3.Max(b,c)));triangles++;
                }
            }
        }
        var min=gates.Aggregate(Vector3.Min)-new Vector3(40);var max=gates.Aggregate(Vector3.Max)+new Vector3(40);
        float sx=(Resolution-1)/(max.X-min.X),sz=(Resolution-1)/(max.Z-min.Z);
        var samples=new byte[Resolution*Resolution];Array.Fill(samples,(byte)127);int covered=0,corridor=0;
        for(int x=0;x<Resolution;x++)for(int z=0;z<Resolution;z++)
        {
            var p=new Vector3(min.X+x/sx,0,min.Z+z/sz);
            if(!roads.TryGetValue(CellAt(p),out var items))continue;
            float nearest=625;
            foreach(var road in items)
            {
                var d=road.B-road.A;float den=d.X*d.X+d.Z*d.Z;if(den<1e-5f)continue;
                float t=Math.Clamp(((p.X-road.A.X)*d.X+(p.Z-road.A.Z)*d.Z)/den,0,1);
                var q=road.A+t*d;float distance=(p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z);
                if(distance<nearest){nearest=distance;p.Y=q.Y;}
            }
            if(nearest>=625)continue;
            corridor++;
            if(Covered(p,roofs)){samples[x*Resolution+z]=0;covered++;}
        }
        if(covered<100||corridor<1000)throw new InvalidDataException("No useful source roof coverage");
        var bytes=Encode(samples,min.X,min.Z,sx,sz);int checkedSamples=0;
        for(int x=0;x<Resolution;x++)for(int z=0;z<Resolution;z++)
        {
            if(Sample(bytes,x,z)!=samples[x*Resolution+z])throw new InvalidDataException("Native CLM tree round-trip failed");checkedSamples++;
        }
        var gateCoverage=gates.Select((p,i)=>new{Gate=i,Position=new[]{p.X,p.Y,p.Z},Covered=Covered(p,roofs),EncodedAO=Sample(bytes,(int)((p.X-min.X)*sx),(int)((p.Z-min.Z)*sz))}).ToArray();
        if(gateCoverage[206].Covered||!gateCoverage[214].Covered)throw new InvalidDataException("First tunnel approach/interior coverage failed");
        string ao=Path.Combine(output,"ao.clm");File.WriteAllBytes(ao,bytes);
        XDocument ReadXml(string path){using var stream=File.OpenRead(path);return XDocument.Parse(new XmlFile(stream).Document.OuterXml);}
        string lightingPath=Input(Files.Inside(candidate,"shared/lighting.xml"));
        var lighting=ReadXml(lightingPath);
        var sourceLight=ReadXml(Input(Files.Inside(venue,"lighting_day.xml")));
        string floor=sourceLight.Descendants("param").Single(p=>p.Attribute("vehicleAmbientScaleMin") is not null).Attribute("vehicleAmbientScaleMin")!.Value;
        lighting.Descendants("param").Single(p=>p.Attribute("vehicleAmbientScaleMin") is not null).SetAttributeValue("vehicleAmbientScaleMin",floor);
        foreach(var (path,hash) in inputs)if(Files.Hash(path)!=hash)throw new InvalidDataException("Vehicle AO source changed");
        string savedLighting=Path.Combine(output,probe?"lighting.xml":"shared/lighting.xml");
        using var xmlSource=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(lighting.ToString()));
        using var encodedLighting=new MemoryStream();new XmlFile(xmlSource).Write(encodedLighting,XmlType.BinXml);
        File.WriteAllBytes(savedLighting,encodedLighting.ToArray());
        string candidatePrefix=Path.GetFullPath(candidate)+Path.DirectorySeparatorChar;
        Files.Json(Path.Combine(output,"vehicle-occlusion.json"),new{Inputs=inputs.Where(p=>!p.Key.StartsWith(candidatePrefix,StringComparison.OrdinalIgnoreCase)).ToDictionary(),
            BaseLightingHash=inputs[lightingPath],Triangles=triangles,CorridorSamples=corridor,CoveredSamples=covered,NativeSamplesChecked=checkedSamples,
            SourceVehicleAmbientFloor=floor,Resolution,NativeRadix=Radix,HdrUnchanged=true,GateCoverage=gateCoverage,
            Approximation="2D scalar ambient occlusion from actual source roof coverage 3-18m above the driving corridor; GRID 2 ambient SH is not reconstructed",RuntimeValidated=false});
        if(probe)Files.Json(Path.Combine(output,"car-lighting-probe.json"),new{SourceCandidate=Path.GetFullPath(candidate),RenderingProbeOnly=true,
            Files=new Dictionary<string,string>{{"ao.clm",Files.Hash(ao)},{"lighting.xml",Files.Hash(savedLighting)}},RuntimeValidated=false});
        Console.WriteLine($"Vehicle AO: {covered:N0} covered samples, {gateCoverage.Count(g=>g.Covered)} covered gates; HDR unchanged");
    }
    static byte[] Encode(byte[] samples,float x,float z,float sx,float sz)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(x);writer.Write(z);writer.Write(sx);writer.Write(sz);writer.Write(Resolution);writer.Write(Radix);
        stream.SetLength(24+NodeBytes);
        byte? Constant(int a,int b,int size)
        {
            if(a>=Resolution||b>=Resolution)return 127;
            byte value=samples[a*Resolution+b];
            for(int i=a;i<Math.Min(Resolution,a+size);i++)for(int j=b;j<Math.Min(Resolution,b+size);j++)if(samples[i*Resolution+j]!=value)return null;
            if((a+size>Resolution||b+size>Resolution)&&value!=127)return null;return value;
        }
        void Node(int offset,int a,int b,int size)
        {
            int cell=size/Radix;var values=new byte[100];var children=new List<(int X,int Z)>();
            for(int i=0;i<Radix;i++)for(int j=0;j<Radix;j++)
            {
                int px=a+i*cell,pz=b+j*cell;var value=Constant(px,pz,cell);
                // Native CLM rows are Z, columns X; the backing raster here is X-major.
                if(value is {} v)values[j*Radix+i]=v;
                else {values[j*Radix+i]=unchecked((byte)(-children.Count-1));children.Add((px,pz));}
            }
            int first=(int)stream.Length;stream.SetLength(first+children.Count*NodeBytes);stream.Position=offset;
            writer.Write(children.Count==0?uint.MaxValue:(uint)first);writer.Write(values);
            for(int i=0;i<children.Count;i++)Node(first+i*NodeBytes,children[i].X,children[i].Z,cell);
        }
        Node(24,0,0,10000);return stream.ToArray();
    }
    static byte Sample(byte[] bytes,int x,int z)
    {
        int size=10000,node=24;
        while(size>1)
        {
            size/=Radix;int a=x/size,b=z/size;x%=size;z%=size;
            int value=unchecked((sbyte)bytes[node+4+b*Radix+a]);if(value>=0)return (byte)value;
            node=checked((int)BitConverter.ToUInt32(bytes,node)+(-value-1)*NodeBytes);
        }
        throw new InvalidDataException("Invalid CLM leaf");
    }
}
