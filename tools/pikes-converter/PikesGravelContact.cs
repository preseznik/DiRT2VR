using System.Numerics;
using System.Xml.Linq;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Xml;

internal static class PikesGravelContact
{
    internal const float GroundRadius=50;
    // Source gates are six metres either side; reset strips extend another 35.
    // Include trunks beyond that envelope without spending native cells on
    // unreachable forest. Ground has a further eight metres of support.
    internal const float TreeRadius=42;

    internal static void AddTrees(string rally,string game,Scene scene,string output,PikesSourceProfile? sourceProfile=null)
    {
        var profile=sourceProfile??PikesSourceProfile.Gravel("day");
        string Input(string name){string path=profile.PathIn(rally,name);scene.Inputs.TryAdd(path,Files.Hash(path));return path;}
        using(var stream=File.OpenRead(Path.Combine(game,"surface_materials.xml")))
            if(!XDocument.Parse(new XmlFile(stream).Document.OuterXml).Descendants("MATERIAL").Any(e=>(string?)e.Attribute("name")=="WOS+"))
                throw new InvalidDataException("Native wood contact material is unavailable");
        var types=XDocument.Load(Input("route_0/route_objecttypes.pssg"));
        var definitions=types.Root!.Element("PSSGDATABASE")!.Elements().ToDictionary(e=>A(e,"id"));
        var models=PikesSponsorBarrier.Read(Input("trees.pssg"));
        var seen=new HashSet<string>();var records=new List<object>();var skipped=new Dictionary<string,int>();
        var gates=scene.Gates.Concat(scene.RunoutGates).ToArray();
        bool Near(Vector3 p)
        {
            for(int i=1;i<gates.Length;i++)
            {
                var a=new Vector2(gates[i-1].Position.X,gates[i-1].Position.Z);var edge=new Vector2(gates[i].Position.X,gates[i].Position.Z)-a;
                float t=edge.LengthSquared()>1e-8f?Math.Clamp(Vector2.Dot(new Vector2(p.X,p.Z)-a,edge)/edge.LengthSquared(),0,1):0;
                if(Vector2.DistanceSquared(new(p.X,p.Z),a+edge*t)<=TreeRadius*TreeRadius)return true;
            }
            return false;
        }
        void Add(string model,Matrix4x4 placement,string label)
        {
            if(!Near(placement.Translation-scene.Origin))return;
            string key=model+":"+string.Join(",",new[]{placement.M11,placement.M12,placement.M13,placement.M21,placement.M22,placement.M23,placement.M31,placement.M32,placement.M33,placement.M41,placement.M42,placement.M43}.Select(v=>MathF.Round(v,4).ToString("R",CultureInfo.InvariantCulture)));
            if(!seen.Add(key))return;
            if(!definitions.TryGetValue(model+".max",out var entity))
            {
                if(!model.StartsWith("pke_tree_dist_",StringComparison.Ordinal))throw new InvalidDataException("Tree has no source collider: "+model);
                skipped["Distant billboard group"]=skipped.GetValueOrDefault("Distant billboard group")+1;return;
            }
            string initial=A(entity.Element("TEMPLATEBEHAVIOUR")!,"typeuri")[1..];
            var active=definitions[initial].Elements("TEMPLATECOMPONENTCHANGE").Where(e=>A(e,"operation")=="add").Select(e=>A(e,"componenturi")[1..]).ToHashSet();
            foreach(var sub in entity.Elements("TEMPLATESUBENTITY").Where(e=>active.Contains(A(e,"id"))))
            {
                var body=sub.Element("TEMPLATERIGIDBODY");if(body is null)continue;
                if(A(body,"infMass")!="1")throw new InvalidDataException("Movable tree source body");
                var transformable=sub.Element("TEMPLATETRANSFORMABLE")!;string uri=A(definitions[A(transformable,"uri")[1..]],"uri");
                if(!uri.StartsWith("trees.pssg#",StringComparison.Ordinal))throw new InvalidDataException("Unexpected tree render frame");
                var node=models.GetObject<PssgNode>(uri.Split('#').Last().AsMemory());
                var world=node.Transform.Transform;
                for(var parent=node.ParentElement;parent is not null;parent=parent.ParentElement)if(parent is PssgNode n)world*=n.Transform.Transform;
                foreach(var reference in body.Elements("TEMPLATERIGIDBODYSHAPE"))
                {
                    var shape=definitions[A(reference,"uri")[1..]];
                    var matrix=Matrix(reference.Element("TEMPLATETRANSFORM")!.Value)*world*placement;
                    if(matrix.GetDeterminant()<=0)throw new InvalidDataException("Reflected tree collider");
                    int sides=shape.Name.LocalName=="TEMPLATESHAPECYLINDER"?6:8;
                    var faces=MizuPrimitives.Triangles(shape,sides,4,true).Select(t=>new QuadTreeDataTriangle(Vector3.Transform(t.A,matrix)-scene.Origin,Vector3.Transform(t.B,matrix)-scene.Origin,Vector3.Transform(t.C,matrix)-scene.Origin,"WOS+")).ToArray();
                    int firstFace=scene.Collision.Count;
                    scene.Collision.AddRange(faces);
                    var min=faces.SelectMany(t=>new[]{t.Position0,t.Position1,t.Position2}).Aggregate(Vector3.Min);
                    var max=faces.SelectMany(t=>new[]{t.Position0,t.Position1,t.Position2}).Aggregate(Vector3.Max);
                    records.Add(new{Model=model,Placement=label,Shape=A(shape,"id"),Kind=shape.Name.LocalName,SourceProperties=A(shape,"props"),FirstFace=firstFace,Triangles=faces.Length,Minimum=new[]{min.X,min.Y,min.Z},Maximum=new[]{max.X,max.Y,max.Z}});
                }
            }
        }
        byte[] bytes=File.ReadAllBytes(Input("route_0/trees.bin"));
        int I(int at)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at,4));
        float F(int at)=>BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at,4));
        if(I(0)!=0||I(8)!=1)throw new InvalidDataException("Unsupported source tree placements");
        int list=I(4),refs=I(list+36),count=I(list+40),instances=I(list+44),n=I(list+48);
        var names=new Dictionary<int,string>();
        for(int i=0;i<count;i++){int p=refs+i*40,at=I(p);names.Add(I(p+4),Encoding.ASCII.GetString(bytes,at,Array.IndexOf(bytes,(byte)0,at)-at));}
        for(int i=0;i<n;i++)
        {
            int p=instances+i*80;
            Add(names[I(p)],new(F(p+8),F(p+12),F(p+16),0,F(p+20),F(p+24),F(p+28),0,F(p+32),F(p+36),F(p+40),0,F(p+44),F(p+48),F(p+52),1),"trees.bin:"+I(p+4));
        }
        var ens=XDocument.Load(Input("route_0/objects.ens"));
        var references=ens.Descendants("TEMPLATEENTITYREFERENCE").ToDictionary(e=>A(e,"id"),e=>A(e,"uri").Split('#').Last().Replace(".max","",StringComparison.Ordinal));
        foreach(var entity in ens.Descendants().Where(e=>e.Name.LocalName is "TEMPLATEENTITYINSTANCE" or "TEMPLATEBASICENTITYINSTANCE"))
        {
            string name=references[A(entity,"uri")[1..]];
            if(!names.Values.Contains(name))continue;
            Add(name,Matrix(entity.Element("TEMPLATETRANSFORM")!.Value),"objects.ens:"+A(entity,"id"));
        }
        if(records.Count==0)throw new InvalidDataException("No source tree colliders reconstructed");
        Files.Json(Path.Combine(output,"tree-collision.json"),new{CorridorMetres=TreeRadius,NativeMaterial="WOS+",SourceDimensionsAndTransformsPreserved=true,CylinderSides=6,SphereSides=8,SphereBands=4,Shapes=records.Count,Records=records,Skipped=skipped,NativeRuntimeValidated=false});
        Console.WriteLine($"Source tree collision: {records.Count:N0} shapes");
    }
    static string A(XElement e,string name)=>(string?)e.Attribute(name)??throw new InvalidDataException("Missing source tree attribute "+name);
    static Matrix4x4 Matrix(string value)
    {
        var a=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        if(a.Length!=16||a.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Invalid source tree matrix");
        return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);
    }
}
