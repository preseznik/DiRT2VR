using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.TrackQuadTree;

internal sealed class MizuPrimitives
{
    sealed record Library(string Path,Dictionary<string,XElement> Entities,Dictionary<string,XElement> Shapes,Dictionary<string,XElement> Renderables);
    readonly Library objects,routeObjects;
    readonly Func<string,string,Matrix4x4> renderWorld;
    readonly Func<Vector3,float,bool> near;
    readonly Scene scene;
    readonly List<object> added=[];
    readonly Dictionary<string,int> skipped=new(StringComparer.Ordinal);
    internal MizuPrimitives(Func<string,string> input,Func<string,string,Matrix4x4> renderWorld,Func<Vector3,float,bool> near,Scene scene)
    {
        this.renderWorld=renderWorld;this.near=near;this.scene=scene;
        objects=Read("objecttypes.pssg");routeObjects=Read("route_0/route_objecttypes.pssg");
        Library Read(string path)
        {
            // These .pssg files are CSSG XML. The binary PSSG schema would
            // interpret unknown string/numeric attributes as hex data.
            var doc=XDocument.Load(input(path));
            return new(path,doc.Descendants("TEMPLATEENTITY").ToDictionary(e=>(string)e.Attribute("id")!),
                doc.Descendants().Where(e=>e.Name.LocalName.StartsWith("TEMPLATESHAPE",StringComparison.Ordinal)).ToDictionary(e=>(string)e.Attribute("id")!),
                doc.Descendants("TEMPLATERENDERABLE").ToDictionary(e=>(string)e.Attribute("id")!));
        }
    }
    void Skip(string reason)=>skipped[reason]=skipped.GetValueOrDefault(reason)+1;
    static string Id(XElement e,string attribute)=>(string?)e.Attribute(attribute)??throw new InvalidDataException("Missing primitive reference: "+attribute);
    static string Local(string value)=>value.StartsWith('#')?value[1..]:throw new InvalidDataException("Expected local primitive reference: "+value);
    static float Positive(XElement shape,string name)
    {
        float value=float.Parse(Id(shape,name),CultureInfo.InvariantCulture);
        if(!float.IsFinite(value)||value<=0)throw new InvalidDataException("Invalid primitive dimension: "+name);
        return value;
    }
    static Matrix4x4 Transform(XElement element)
    {
        var f=(element.Element("TEMPLATETRANSFORM")?.Value??throw new InvalidDataException("Missing shape transform"))
            .Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
        if(f.Length!=16||f.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Invalid primitive transform");
        var m=new Matrix4x4(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]);
        if(m.GetDeterminant()<=0||MathF.Abs(m.M14)+MathF.Abs(m.M24)+MathF.Abs(m.M34)>1e-5f||MathF.Abs(m.M44-1)>1e-5f)
            throw new InvalidDataException("Reflected, singular or non-affine primitive transform");
        return m;
    }
    internal void Add(string model,string asset,Matrix4x4 placement,string label)
    {
        var library=asset=="objects.pssg"?objects:routeObjects;
        if(!library.Entities.TryGetValue(model+".max",out var entity))
        {
            // Route variants can keep their render mesh in objects.pssg while
            // declaring their entity and collision shapes in the route library.
            // Route meshes are distinct exports; their same-named base
            // templates can refer to render frames absent from the route mesh.
            if(asset!="objects.pssg"&&asset!="trees.pssg"){Skip("No source primitive template");return;}
            var other=ReferenceEquals(library,objects)?routeObjects:objects;
            if(!other.Entities.TryGetValue(model+".max",out entity)){Skip("No source primitive template");return;}
            library=other;
        }
        foreach(var subentity in entity.Elements("TEMPLATESUBENTITY"))
        foreach(var body in subentity.Elements("TEMPLATERIGIDBODY"))
        {
            if(Id(body,"infMass")!="1"){Skip("Movable body retained as visual only");continue;}
            var transformable=subentity.Element("TEMPLATETRANSFORMABLE")??throw new InvalidDataException("Primitive body has no render frame");
            var renderable=library.Renderables[Local(Id(transformable,"uri"))];
            string uri=Id(renderable,"uri");int separator=uri.IndexOf('#');
            if(separator<=0)throw new InvalidDataException("Invalid primitive render frame URI");
            string path=uri[..separator];
            if(path=="route_objects.pssg")path="route_0/"+path;
            var frame=renderWorld(path,uri[(separator+1)..])*placement;
            foreach(var reference in body.Elements("TEMPLATERIGIDBODYSHAPE"))
            {
                string shapeId=Local(Id(reference,"uri"));
                if(!library.Shapes.TryGetValue(shapeId,out var shape)){Skip("Source mesh shape has no primitive definition");continue;}
                string props=Id(shape,"props");
                if(props.EndsWith("#sSoftVegetation",StringComparison.Ordinal)){Skip("Soft vegetation is not a solid obstacle");continue;}
                var transform=Transform(reference)*frame;
                var triangles=Triangles(shape).Select(t=>new QuadTreeDataTriangle(Vector3.Transform(t.A,transform)-scene.Origin,
                    Vector3.Transform(t.B,transform)-scene.Origin,Vector3.Transform(t.C,transform)-scene.Origin,"METL")).ToArray();
                if(!triangles.Any(t=>near(t.Position0,25)||near(t.Position1,25)||near(t.Position2,25)||near((t.Position0+t.Position1+t.Position2)/3,25)))
                {Skip("Outside 25 metre driving corridor");continue;}
                scene.Collision.AddRange(triangles);
                var points=triangles.SelectMany(t=>new[]{t.Position0,t.Position1,t.Position2}).ToArray();
                var minimum=points.Aggregate(Vector3.Min);var maximum=points.Aggregate(Vector3.Max);
                var centre=Vector3.Transform(Vector3.Zero,transform)-scene.Origin;
                added.Add(new{Model=model,Placement=label,Library=library.Path,Shape=shapeId,Kind=shape.Name.LocalName,SourceProperties=props,
                    SourceDimensions=shape.Attributes().Where(a=>a.Name.LocalName is "width" or "height" or "length" or "radius").ToDictionary(a=>a.Name.LocalName,a=>float.Parse(a.Value,CultureInfo.InvariantCulture)),
                    Centre=new[]{centre.X,centre.Y,centre.Z},Minimum=new[]{minimum.X,minimum.Y,minimum.Z},Maximum=new[]{maximum.X,maximum.Y,maximum.Z},TriangleCount=triangles.Length,
                    Transform=new[]{transform.M11,transform.M12,transform.M13,transform.M14,transform.M21,transform.M22,transform.M23,transform.M24,
                        transform.M31,transform.M32,transform.M33,transform.M34,transform.M41,transform.M42,transform.M43,transform.M44}});
            }
        }
    }
    internal void Write(string output)=>Files.Json(Path.Combine(output,"primitive-collision.json"),new{StaticBodiesOnly=true,CorridorMetres=25,
        SuppliedShellPaddingIgnored=true,PrimitiveMaterial="METL",CylinderSides=16,SphereLatitudeBands=8,
        AddedShapes=added.Count,Shapes=added,Skipped=skipped,DynamicBehaviorReproduced=false,RuntimeValidated=false});

    internal static IEnumerable<(Vector3 A,Vector3 B,Vector3 C)> Triangles(XElement shape)
    {
        var vertices=new List<Vector3>();var faces=new List<(int,int,int)>();
        switch(shape.Name.LocalName)
        {
            case "TEMPLATESHAPEBOX":
                float x=Positive(shape,"width")/2,y=Positive(shape,"height")/2,z=Positive(shape,"length")/2;
                vertices.AddRange([new(-x,-y,-z),new(x,-y,-z),new(x,y,-z),new(-x,y,-z),new(-x,-y,z),new(x,-y,z),new(x,y,z),new(-x,y,z)]);
                faces.AddRange([(0,1,2),(0,2,3),(4,6,5),(4,7,6),(0,4,5),(0,5,1),(3,2,6),(3,6,7),(0,3,7),(0,7,4),(1,5,6),(1,6,2)]);
                break;
            case "TEMPLATESHAPECYLINDER":
                float radius=Positive(shape,"radius"),height=Positive(shape,"height")/2;
                for(int ring=0;ring<2;ring++)for(int i=0;i<16;i++)
                {float angle=i*MathF.Tau/16;vertices.Add(new(radius*MathF.Cos(angle),ring==0?-height:height,radius*MathF.Sin(angle)));}
                vertices.Add(new(0,-height,0));vertices.Add(new(0,height,0));
                for(int i=0;i<16;i++){int j=(i+1)%16;faces.AddRange([(i,j,j+16),(i,j+16,i+16),(32,j,i),(33,i+16,j+16)]);}
                break;
            case "TEMPLATESHAPESPHERE":
                float sphereRadius=Positive(shape,"radius");
                vertices.Add(new(0,sphereRadius,0));
                for(int ring=1;ring<8;ring++)for(int i=0;i<16;i++)
                {float latitude=ring*MathF.PI/8,angle=i*MathF.Tau/16;vertices.Add(new(sphereRadius*MathF.Sin(latitude)*MathF.Cos(angle),sphereRadius*MathF.Cos(latitude),sphereRadius*MathF.Sin(latitude)*MathF.Sin(angle)));}
                int bottom=vertices.Count;vertices.Add(new(0,-sphereRadius,0));
                for(int i=0;i<16;i++)
                {
                    int j=(i+1)%16;faces.Add((0,1+i,1+j));faces.Add((bottom,97+j,97+i));
                    for(int ring=0;ring<6;ring++){int a=1+ring*16+i,b=1+ring*16+j;faces.AddRange([(a,a+16,b+16),(a,b+16,b)]);}
                }
                break;
            default:throw new InvalidDataException("Unsupported primitive: "+shape.Name);
        }
        foreach(var(a,b,c)in faces)
        {
            var normal=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
            if(normal.LengthSquared()<1e-12f)throw new InvalidDataException("Degenerate source primitive");
            yield return Vector3.Dot(normal,vertices[a]+vertices[b]+vertices[c])>0?(vertices[a],vertices[b],vertices[c]):(vertices[a],vertices[c],vertices[b]);
        }
    }
}
