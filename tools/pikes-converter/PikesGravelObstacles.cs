using System.Numerics;
using System.Globalization;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class PikesGravelObstacles
{
    static string A(XElement e,string name)=>(string?)e.Attribute(name)??throw new InvalidDataException("Missing "+name);
    static Matrix4x4 Matrix(string value){var a=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();if(a.Length!=16)throw new InvalidDataException("Invalid source matrix");return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
    static Matrix4x4 World(PssgNode node){var m=node.Transform.Transform;for(var p=node.ParentElement;p is not null;p=p.ParentElement)if(p is PssgNode n)m*=n.Transform.Transform;return m;}
    static IEnumerable<PssgNode> Nodes(PssgNode node)=>new[]{node}.Concat(node.ChildElements.OfType<PssgNode>().SelectMany(Nodes));
    internal static void Add(string rally,PikesSourceProfile profile,Scene scene,string output)
    {
        string Input(string name){string path=profile.PathIn(rally,name);scene.Inputs.TryAdd(path,Files.Hash(path));return path;}
        var ens=XDocument.Load(Input("route_0/objects.ens"));var types=XDocument.Load(Input("route_0/route_objecttypes.pssg"));
        var source=PikesSponsorBarrier.Read(Input("objects.pssg"));
        var references=ens.Descendants("TEMPLATEENTITYREFERENCE").ToDictionary(e=>A(e,"id"),e=>A(e,"uri"));
        var definitions=types.Root!.Element("PSSGDATABASE")!.Elements().ToDictionary(e=>A(e,"id"));
        var shapes=new List<object>();int instances=0;
        foreach(var placement in ens.Descendants().Where(e=>e.Name.LocalName is "TEMPLATEENTITYINSTANCE" or "TEMPLATEBASICENTITYINSTANCE")
            .Where(e=>A(e,"uri").StartsWith("#pke_armco_",StringComparison.Ordinal)||A(e,"uri").StartsWith("#core_brand_scaffolding_",StringComparison.Ordinal)||A(e,"uri") is "#pke_fence_wire_main_a" or "#pke_fence_wire_end_a"))
        {
            string model=references[A(placement,"uri")[1..]].Split('#').Last();var entity=definitions[model];
            string initial=A(entity.Elements("TEMPLATEBEHAVIOUR").Single(),"typeuri")[1..];
            var active=definitions[initial].Elements("TEMPLATECOMPONENTCHANGE").Where(e=>A(e,"operation")=="add").Select(e=>A(e,"componenturi")[1..]).ToHashSet();
            foreach(var sub in entity.Elements("TEMPLATESUBENTITY").Where(e=>active.Contains(A(e,"id"))||model.StartsWith("pke_fence_wire_",StringComparison.Ordinal)))
            {
                var body=sub.Element("TEMPLATERIGIDBODY");if(body is null)continue;
                if(A(body,"infMass")!="1")throw new InvalidDataException("Barrier source body is movable");
                var frameRef=sub.Element("TEMPLATETRANSFORMABLE")!;string uri=A(definitions[A(frameRef,"uri")[1..]],"uri");
                var root=source.GetObject<PssgNode>(uri.Split('#').Last().AsMemory());
                // Scaffold boxes use the render frame directly; armco boxes
                // are relative to the named body inside its articulated model.
                var bodyFrame=model.StartsWith("pke_armco_",StringComparison.Ordinal)?Nodes(root).Single(n=>n.Id==A(frameRef,"sid")):root;
                var world=World(bodyFrame)*Matrix(placement.Element("TEMPLATETRANSFORM")!.Value);
                foreach(var reference in body.Elements("TEMPLATERIGIDBODYSHAPE"))
                {
                    var shape=definitions[A(reference,"uri")[1..]];var transform=Matrix(reference.Element("TEMPLATETRANSFORM")!.Value)*world;
                    foreach(var face in MizuPrimitives.Triangles(shape))scene.Collision.Add(new(Vector3.Transform(face.A,transform)-scene.Origin,Vector3.Transform(face.B,transform)-scene.Origin,Vector3.Transform(face.C,transform)-scene.Origin,"METL"));
                    shapes.Add(new{Instance=A(placement,"id"),Model=model,Shape=A(shape,"id")});
                }
            }
            instances++;
        }
        Files.Json(Path.Combine(output,"source-fixed-barriers.json"),new{Instances=instances,Shapes=shapes,SourceBodyFramesPreserved=true});
    }
}
