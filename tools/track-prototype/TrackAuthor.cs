using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;
using SharpGLTF.Schema2;

internal static class TrackAuthor
{
    sealed class WriterState : PssgModelWriterState { }
    internal record Gate(float[] position, float[] tangent, double distance);
    internal record Mesh(string name, string surface, float[][] vertices, int[][] triangles);
    internal record Spec(int schema, double length, double width, Gate[] points, Mesh[] meshes);
    static Vector3 V(float[] p) => new(p[0],p[1],p[2]);
    static string F(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);
    static XElement Vec(string name, Vector3 p) => new(name,new XAttribute("format","float3"),$"{F(p.X)} {F(p.Y)} {F(p.Z)}");
    static PssgFile Open(string path) { using var s=File.OpenRead(path); return PssgFile.Open(s); }
    static void Save(PssgFile file,string path) { using var s=new FileStream(path,FileMode.CreateNew); file.Save(s); }
    static void Xml(XDocument doc,string path)
    {
        using var input=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(doc.ToString()));
        var file=new XmlFile(input); using var output=new FileStream(path,FileMode.CreateNew); file.Write(output,XmlType.BinXml);
    }
    internal static void Build(string source, string authored, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        var spec=JsonSerializer.Deserialize<Spec>(File.ReadAllText(Path.Combine(authored,"mesh.json")))!;
        if(spec.schema!=1 || spec.points.Length<8 || spec.width!=10 || !double.IsFinite(spec.length)) throw new InvalidDataException("Unsupported authoring contract.");
        Directory.CreateDirectory(output);
        var ground=GltfTrackGroundConverter.Convert(ModelRoot.Load(Path.Combine(authored,"collision.glb")),VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
        using(var s=File.Create(Path.Combine(output,"track.jpk"))) ground.Save().Write(s);
        TrackGroundGltfConverter.Convert(ground).SaveGLB(Path.Combine(output,"ground-readback.glb"));
        WriteVisuals(source,output,spec);
        WriteRoute(source,output,spec);
        TrackVisibility.Write(Path.Combine(source,"track.vis"),Path.Combine(output,"track.vis"),
            spec.meshes.SelectMany(m=>m.vertices).Select(V).Aggregate(Vector3.Min),
            spec.meshes.SelectMany(m=>m.vertices).Select(V).Aggregate(Vector3.Max));
        Scenery.Write(source,output);
        Validate(authored,output,spec);
        File.WriteAllText(Path.Combine(output,"authoring.json"),JsonSerializer.Serialize(new { schema=1, track="d2vr_test", route="route_0", length=spec.length, runtimeValidated=false, visibility="Terrain tile bounds expanded; all donor PVS leaves use all-visible masks", scenery="Local placements suppressed; distant backdrop, sky and trees retained" },new JsonSerializerOptions{WriteIndented=true}));
    }
    static void Validate(string authored,string output,Spec spec)
    {
        var original=GeometryCheck.Read(Path.Combine(authored,"collision.glb"));
        var rebuilt=GeometryCheck.Read(Path.Combine(output,"ground-readback.glb"));
        if(GeometryCheck.Unmatched(original,rebuilt)!=0 || GeometryCheck.Unmatched(rebuilt,original)!=0) throw new InvalidDataException("Generated collision differs from Blender geometry.");
        var expected=spec.meshes.SelectMany(m=>m.triangles.Select(t=>new GeometryCheck.Triangle(V(m.vertices[t[0]]),V(m.vertices[t[1]]),V(m.vertices[t[2]]),"visual"))).ToList();
        var pssg=Open(Path.Combine(output,"routesplit.pssg"));
        VisualCheck.Validate(pssg);
        var reader=new RenderDataSourceReader(pssg.GetObject<PssgRenderDataSource>("prototype_road".AsMemory()));
        var actual=new List<GeometryCheck.Triangle>();
        for(int i=0;i<reader.IndexCount;i+=3) actual.Add(new(reader.GetPosition(reader.GetIndex(i)),reader.GetPosition(reader.GetIndex(i+1)),reader.GetPosition(reader.GetIndex(i+2)),"visual"));
        if(expected.Count!=actual.Count || GeometryCheck.Unmatched(expected,actual)!=0 || GeometryCheck.Unmatched(actual,expected)!=0) throw new InvalidDataException("PSSG visual geometry differs from authoring input.");
        if(original.Count!=expected.Count) throw new InvalidDataException("Blender omitted authored triangles.");
        var expectedCollision=spec.meshes.SelectMany(m=>m.triangles.Select(t=>new GeometryCheck.Triangle(V(m.vertices[t[0]]),V(m.vertices[t[1]]),V(m.vertices[t[2]]),m.surface))).ToList();
        if(GeometryCheck.Unmatched(expectedCollision,original)!=0 || GeometryCheck.Unmatched(original,expectedCollision)!=0) throw new InvalidDataException("Blender coordinate or surface mismatch.");
        var grid=Open(Path.Combine(output,"grids.pssg"));
        var slot=grid.Elements<PssgNode>().Single(x=>x.Id=="slot_00").Transform.Transform;
        if(MathF.Abs(slot.GetDeterminant()-1)>0.001f) throw new InvalidDataException("Start grid is reflected or scaled.");
        foreach(var name in new[]{"ai_track.xml","progress_track.xml","ai_vehicle_track.xml","route_overrides.xml"})
        {
            using var input=File.OpenRead(Path.Combine(output,name)); var xml=new XmlFile(input);
            if(xml.Document.DocumentElement is null) throw new InvalidDataException("Empty route data.");
        }
        var hashes=Directory.GetFiles(output).Where(p=>new[]{".pssg",".jpk",".xml",".cqtc",".vis",".ens",".bin"}.Contains(Path.GetExtension(p))).Order().ToDictionary(p=>Path.GetFileName(p),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        File.WriteAllText(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(new { Passed=true, RuntimeValidated=false, AuthoredTriangles=expected.Count, CollisionTriangles=rebuilt.Count, GateCount=spec.points.Length, LengthMetres=spec.length, GeometryToleranceMetres=0.02, SurfaceAndWindingChecked=true, GridDeterminant=slot.GetDeterminant(), Files=hashes },new JsonSerializerOptions { WriteIndented=true }));
    }
    static void WriteVisuals(string source,string output,Spec spec)
    {
        var file=Open(Path.Combine(source,"routesplit.pssg"));
        var layout=ShaderInputInfo.CreateFromPssg(file).ToDictionary(x=>x.ShaderGroupId);
        var rdsLib=file.Elements<PssgLibrary>().Single(x=>x.Type=="SEGMENTSET");
        var dataLib=file.Elements<PssgLibrary>().Single(x=>x.Type=="RENDERINTERFACEBOUND");
        // Keep the donor's named tile hierarchy, replacing its drawable streams only.
        var high=file.Elements<PssgRenderNode>().Single(x=>x.Id=="HIGH_4_4");
        var low=file.Elements<PssgRenderNode>().Single(x=>x.Id=="LOW_4_4");
        var shader=file.GetObject<PssgShaderInstance>("dust to dlightdustsmask!0".AsMemory());
        var input=layout[shader.ShaderGroup[1..]];
        var min=new Vector3(float.MaxValue); var max=new Vector3(float.MinValue);
        var writer=new RenderDataSourceWriter("prototype_road");
        foreach(var mesh in spec.meshes)
        {
            var offset=writer.Positions.Count;
            var normals=new Vector3[mesh.vertices.Length];
            foreach(var triangle in mesh.triangles)
            {
                var normal=Vector3.Cross(V(mesh.vertices[triangle[1]])-V(mesh.vertices[triangle[0]]),V(mesh.vertices[triangle[2]])-V(mesh.vertices[triangle[0]]));
                foreach(var index in triangle) normals[index]+=normal;
            }
            for(int vertex=0;vertex<mesh.vertices.Length;vertex++)
            {
                var v=mesh.vertices[vertex];
                var p=V(v); min=Vector3.Min(min,p); max=Vector3.Max(max,p);
                writer.Positions.Add(p); writer.Normals.Add(normals[vertex].LengthSquared()>0?Vector3.Normalize(normals[vertex]):Vector3.UnitY); writer.Tangents.Add(new Vector4(1,0,0,1));
                writer.Colors.Add(Vector4.One);
                var uv=new Vector2(p.X/8,p.Z/8);
                writer.TexCoords0.Add(uv); writer.TexCoords1.Add(uv); writer.TexCoords2.Add(uv); writer.TexCoords3.Add(uv);
            }
            foreach(var triangle in mesh.triangles) foreach(var i in triangle) writer.Indices.Add(checked((uint)(offset+i)));
        }
        if(min.X < -12 || max.X > 208 || min.Z < -67 || max.Z > 154) throw new InvalidDataException("Prototype exceeds donor tile X/Z footprint.");
        foreach(var instance in file.Elements<PssgRenderInstance>().ToArray()) instance.ParentElement!.RemoveChild(instance);
        rdsLib.RemoveChildElements(); dataLib.RemoveChildElements();
        var segment=new PssgSegmentSet(file,rdsLib){Id="prototype_segments",SegmentCount=1}; rdsLib.AppendChild(segment);
        writer.Write(input,segment,dataLib,new WriterState());
        // The car-oriented writer derives the index ID by replacing "RDS" in its
        // name. Our name has no such token, so assign a distinct ID explicitly.
        var dataSource=segment.Segments.Single();
        dataSource.IndexSource!.Id="prototype_road_indices";
        // DiRT 2 terrain declares the primitive on both the source and its indices.
        dataSource.Primitive="triangles";
        foreach(var node in new[]{high,low})
        {
            node.Transform.Transform=Matrix4x4.Identity; node.BoundingBox.BoundsMin=min; node.BoundingBox.BoundsMax=max;
            var instance=new PssgRenderStreamInstance(file,node){Id="prototype_"+node.Id,SourceCount=1,Indices="#prototype_road",StreamCount=0,Shader="#"+shader.Id};
            node.AppendChild(instance);
            instance.AppendChild(new PssgRenderInstanceSource(file,instance){Source="#prototype_road"});
        }
        Save(file,Path.Combine(output,"routesplit.pssg"));
        // Prove the saved container still resolves every new render reference.
        var check=Open(Path.Combine(output,"routesplit.pssg"));
        foreach(var instance in check.Elements<PssgRenderStreamInstance>()) _=instance.GetRenderDataSource();
    }
    static void WriteRoute(string source,string output,Spec spec)
    {
        var n=spec.points.Length;
        var gates=new XElement("gates",new XAttribute("num_gates",n));
        var progress=new XElement("gates",new XAttribute("num_gates",n));
        var links=new XElement("links",new XAttribute("num_links",n));
        for(int i=0;i<n;i++)
        {
            var g=spec.points[i]; var p=V(g.position); var t=V(g.tangent); var left=new Vector3(t.Z,0,-t.X);
            gates.Add(new XElement("gate",new XAttribute("id",i),Vec("position",p+left*5),Vec("normal",-left),
                new XElement("waypoints",new XAttribute("num_waypoints",5),
                    Waypoint(0,"left_track_limit",0),Waypoint(1,"racing_line",5),Waypoint(2,"right_track_limit",10),Waypoint(3,"left_racing_limit",1),Waypoint(4,"right_racing_limit",9)),
                new XElement("edges",new XAttribute("num_edges",0))));
            // Place finish last, just behind the grid; other gates increase along the loop.
            int k=(i+1)%n; var pg=spec.points[k]; var pt=V(pg.tangent); var pl=new Vector3(pt.Z,0,-pt.X); var pp=V(pg.position);
            progress.Add(new XElement("gate",new XAttribute("id",i),new XAttribute("distance",F(i==n-1?spec.length:pg.distance)),Vec("left",pp+pl*6),Vec("right",pp-pl*6)));
            links.Add(new XElement("link",new XAttribute("id",i),new XAttribute("from_gate",i),new XAttribute("to_gate",(i+1)%n)));
        }
        var ai=new XDocument(new XElement("ai_track_data",new XAttribute("version_major",3),new XAttribute("version_minor",0),new XAttribute("version_revision",2),
            new XElement("track",new XAttribute("name","default"),gates,links,
                new XElement("brake_lines",new XAttribute("num_brake_lines",0)),new XElement("min_speed_lines",new XAttribute("num_min_speed_lines",0)),
                new XElement("retire_lines",new XAttribute("num_retire_lines",0)),new XElement("fork_sets",new XAttribute("num_fork_sets",0)))));
        Xml(ai,Path.Combine(output,"ai_track.xml")); Xml(ai,Path.Combine(output,"dev_ai_track.xml"));
        var splits=new XElement("route",new XAttribute("id",0),new XAttribute("direction","forwards"),new XAttribute("num_splits",3),
            new XElement("split",new XAttribute("id",0),new XAttribute("type","time"),new XAttribute("gate",n/3)),
            new XElement("split",new XAttribute("id",1),new XAttribute("type","time"),new XAttribute("gate",2*n/3)),
            new XElement("split",new XAttribute("id",2),new XAttribute("type","finish"),new XAttribute("gate",n-1)));
        Xml(new XDocument(new XElement("progress_track_data",new XAttribute("exporter_version","3.0.0"),new XElement("track",new XAttribute("type","circuit"),new XAttribute("total_distance",F(spec.length))),new XElement("routes",new XAttribute("num_routes",1),splits),progress)),Path.Combine(output,"progress_track.xml"));
        var grid=Open(Path.Combine(source,"grids.pssg"));
        foreach(var node in grid.Elements<PssgNode>())
        {
            node.Transform.Transform=Matrix4x4.Identity;
            if(node.Id.StartsWith("slot_",StringComparison.Ordinal))
            {
                var t=V(spec.points[0].tangent); var p=V(spec.points[0].position)+t*0.5f;
                // EGO cars face local -Z; keep the authored start above the road.
                var right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,-t));
                node.Transform.Transform=new Matrix4x4(right.X,right.Y,right.Z,0,0,1,0,0,-t.X,-t.Y,-t.Z,0,p.X,p.Y+0.5f,p.Z,1);
            }
        }
        Save(grid,Path.Combine(output,"grids.pssg"));
        using(var stream=File.OpenRead(Path.Combine(source,"ai_vehicle_track.xml")))
        {
            var file=new XmlFile(stream);
            foreach(XmlElement b in file.Document.SelectNodes("//brake_lines")!) { b.RemoveAll(); b.SetAttribute("num_brake_lines","0"); }
            using var dest=File.Create(Path.Combine(output,"ai_vehicle_track.xml")); file.Write(dest);
        }
        Xml(new XDocument(new XElement("route",new XElement("default",new XAttribute("track_cull_dist","1000.0"),new XAttribute("world_cull_dist","2000.0"),new XAttribute("track_lod_dist","500.0"),new XAttribute("fade","10.0")),new XElement("Systems",new XAttribute("tree_settings","low"),new XAttribute("ornament_settings","medium")))),Path.Combine(output,"route_overrides.xml"));
        WriteBoundaries(output,spec);
    }
    static XElement Waypoint(int id,string type,int length) => new("waypoint",new XAttribute("id",id),new XAttribute("type",type),new XAttribute("length",length),type=="racing_line"?new XElement("racing_line",new XAttribute("type","optimal")):null);
    static void WriteBoundaries(string output,Spec spec)
    {
        // Use source material semantics for solid collision; reset/camera semantics remain donor-specific.
        var builder=new QuadTreeMeshDataBuilder(CQuadTreeTypeInfo.Get(CQuadTreeType.Dirt));
        for(int i=0;i<spec.points.Length;i++)
        foreach(var side in new[]{-1,1})
        {
            int j=(i+1)%spec.points.Length;
            Vector3 Edge(Gate g) {var p=V(g.position);var t=V(g.tangent);return p+new Vector3(-t.Z,0,t.X)*(9*side);}
            var a=Edge(spec.points[i]);var b=Edge(spec.points[j]);var c=b+Vector3.UnitY*1.2f;var d=a+Vector3.UnitY*1.2f;
            builder.Add(new QuadTreeDataTriangle(a,b,c,"FBND")); builder.Add(new QuadTreeDataTriangle(a,c,d,"FBND"));
            builder.Add(new QuadTreeDataTriangle(c,b,a,"FBND")); builder.Add(new QuadTreeDataTriangle(d,c,a,"FBND"));
        }
        var tree=CQuadTreeFile.Create(builder.Build()); File.WriteAllBytes(Path.Combine(output,"boundarylines.cqtc"),tree.Bytes);
        // A catch plane below the course uses the donor's distinct reset/camera material codes.
        foreach(var (name,material,height) in new[]{("resetlines.cqtc","RESE",-2f),("cameralines.cqtc","RESD",0f)})
        {
            var plane=new QuadTreeMeshDataBuilder(CQuadTreeTypeInfo.Get(CQuadTreeType.Dirt));
            var a=new Vector3(30,height,-50); var b=new Vector3(30,height,130);var c=new Vector3(130,height,130);var d=new Vector3(130,height,-50);
            plane.Add(new QuadTreeDataTriangle(a,b,c,material)); plane.Add(new QuadTreeDataTriangle(a,c,d,material));
            File.WriteAllBytes(Path.Combine(output,name),CQuadTreeFile.Create(plane.Build()).Bytes);
        }
    }
}
