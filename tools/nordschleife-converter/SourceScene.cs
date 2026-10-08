using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using EgoEngineLibrary.Formats.TrackQuadTree;

namespace DiRT2VR.Nordschleife;

internal sealed record Gate(Vector3 Position, Vector3 Tangent, float Left, float Right, float Distance);
internal readonly record struct Cell(int X, int Z);
internal readonly record struct Bounds(Vector3 Min, Vector3 Max)
{
    internal Bounds Union(Bounds other) => new(Vector3.Min(Min,other.Min),Vector3.Max(Max,other.Max));
    internal bool Contains(Bounds other, float tolerance = .02f) => other.Min.X >= Min.X-tolerance && other.Min.Y >= Min.Y-tolerance && other.Min.Z >= Min.Z-tolerance && other.Max.X <= Max.X+tolerance && other.Max.Y <= Max.Y+tolerance && other.Max.Z <= Max.Z+tolerance;
}
internal sealed class Scene
{
    internal readonly List<Mesh> Visuals = [];
    internal readonly List<QuadTreeDataTriangle> Collision = [];
    internal readonly Dictionary<string, Texture> Textures = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, string> Inputs = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, long> Surfaces = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, string> CollisionMappings = new(StringComparer.Ordinal);
    internal readonly HashSet<string> RoadSurfaces = new(StringComparer.Ordinal) { "RDT+" };
    internal Gate[] Gates = [];
    internal Gate[] RunoutGates = [];
    internal Vector3 Origin;
    internal float Length;
    internal bool FullCourse;
    internal readonly List<object> SourceMeshes = [];
    internal const int TileSize = 128;
    internal const float CorridorRadius = 100;
    internal static bool IsGameMarker(string name) => new[]{"AC_START_","AC_PIT_","AC_TIME_","AC_HOTLAP_","AC_AUDIO_"}
        .Any(prefix=>name.StartsWith(prefix,StringComparison.Ordinal));
    internal static Cell Tile(Vector3 p) => new((int)MathF.Floor(p.X / TileSize), (int)MathF.Floor(p.Z / TileSize));

    internal static Scene Read(string acRoot, float requestedLength,bool fullCourse=false)
    {
        if (!fullCourse && (!float.IsFinite(requestedLength) || requestedLength is < 500 or > 1000)) throw new ArgumentException("The segment lab gate supports 500–1000 metres.");
        var scene = new Scene { FullCourse=fullCourse };
        string root = Files.Inside(acRoot, "content/tracks/ks_nordschleife");
        string Input(string relative)
        {
            string path = Files.Inside(root, relative); scene.Inputs[path] = Files.Hash(path); return path;
        }
        var models = Ini(File.ReadAllText(Input("models_nordschleife.ini")));
        _ = Input("nordschleife/data/surfaces.ini");
        using (var r = new BinaryReader(File.OpenRead(Input("nordschleife/ai/fast_lane.ai"))))
        {
            if (r.ReadInt32() != 7) throw new InvalidDataException("Unsupported driving spline version.");
            int n = r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
            if (n is < 10 or > 100000) throw new InvalidDataException("Invalid driving spline point count.");
            var points = new Vector3[n];
            for (int i = 0; i < n; i++) { points[i] = new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()); r.ReadSingle(); r.ReadInt32(); if (!Kn5.Finite(points[i])) throw new InvalidDataException("Nonfinite spline point."); }
            if (r.ReadInt32() != n) throw new InvalidDataException("Missing driving spline side widths.");
            var sides = new (float L,float R)[n];
            for (int i = 0; i < n; i++)
            {
                var values = new float[18]; for (int j = 0; j < 18; j++) values[j] = r.ReadSingle();
                sides[i] = (values[5],values[6]);
                if (!float.IsFinite(sides[i].L) || !float.IsFinite(sides[i].R) || sides[i].L <= 0 || sides[i].R <= 0) throw new InvalidDataException("Invalid spline side widths.");
            }
            // Preserve metres, handedness and elevation; translation only puts the start near the origin.
            scene.Origin = points[0] - Vector3.UnitY * 8;
            var gates = new List<Gate>(); float distance = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) distance += Vector3.Distance(points[i-1],points[i]);
                if (!fullCourse && distance > requestedLength + 75) break;
                if (i > 0 && distance - gates[^1].Distance < 5) continue;
                var tangent = Vector3.Normalize(points[(i+1)%n] - points[(i+n-1)%n]);
                if (!Kn5.Finite(tangent)) throw new InvalidDataException("Invalid driving spline tangent.");
                gates.Add(new(points[i]-scene.Origin,tangent,sides[i].L,sides[i].R,distance));
            }
            scene.Gates = (fullCourse?gates:gates.Where(g => g.Distance <= requestedLength)).ToArray();
            scene.Length = fullCourse?distance+Vector3.Distance(points[^1],points[0]):scene.Gates[^1].Distance;
            scene.RunoutGates = fullCourse?[]:gates.Where(g => g.Distance > scene.Length).ToArray();
        }
        var corridor = new Dictionary<Cell,List<Vector3>>();
        foreach (var g in scene.Gates)
        {
            var cell = Tile(g.Position);
            if (!corridor.TryGetValue(cell, out var list)) corridor[cell] = list = [];
            list.Add(g.Position);
        }
        bool Near(Vector3 p, float radius)
        {
            var cell = Tile(p); int reach = (int)MathF.Ceiling(radius/TileSize);
            for (int dx=-reach;dx<=reach;dx++) for(int dz=-reach;dz<=reach;dz++)
                if (corridor.TryGetValue(new(cell.X+dx,cell.Z+dz),out var list))
                    foreach (var g in list) if ((g.X-p.X)*(g.X-p.X)+(g.Z-p.Z)*(g.Z-p.Z) <= radius*radius && MathF.Abs(g.Y-p.Y)<180) return true;
            return false;
        }
        foreach (var (section, values) in models.Where(x => x.Key.StartsWith("MODEL_",StringComparison.Ordinal)))
        {
            var position = Vector(values["POSITION"]); var rotation = Vector(values["ROTATION"]);
            if (rotation != Vector3.Zero) throw new InvalidDataException("Standard layout unexpectedly has rotated model placement: " + section);
            string path = Input(values["FILE"]); Console.WriteLine("Reading " + Path.GetFileName(path));
            Kn5.Read(path,Matrix4x4.CreateTranslation(position-scene.Origin),scene.Textures, mesh =>
            {
                bool gameMarker=IsGameMarker(mesh.Name);
                var selected = new List<ushort>();
                for (int i=0;i<mesh.Indices.Length;i+=3)
                {
                    var a=mesh.Positions[mesh.Indices[i]];var b=mesh.Positions[mesh.Indices[i+1]];var c=mesh.Positions[mesh.Indices[i+2]];
                    bool InCorridor(float radius)=>Near((a+b+c)/3,radius)||Near(a,radius)||Near(b,radius)||Near(c,radius);
                    if (mesh.Active && mesh.Physical && (fullCourse || InCorridor(CorridorRadius)))
                    {
                        string surface=Surface(mesh.Name); scene.Collision.Add(new(a,b,c,surface));
                        scene.CollisionMappings[mesh.Name] = surface;
                        scene.Surfaces[surface]=scene.Surfaces.GetValueOrDefault(surface)+1;
                    }
                    // Numeric source meshes can supply both collision and visible asphalt/kerbs.
                    if(mesh.Visible&&!gameMarker&&(fullCourse || InCorridor(CorridorRadius)))selected.AddRange([mesh.Indices[i],mesh.Indices[i+1],mesh.Indices[i+2]]);
                }
                if (selected.Count>0) scene.Visuals.Add(Compact(mesh,selected));
                if(fullCourse)scene.SourceMeshes.Add(new{File=values["FILE"],mesh.SourceNode,mesh.Name,mesh.Active,mesh.Visible,mesh.Physical,mesh.LodIn,mesh.LodOut,GameMarker=gameMarker,SourceTriangles=mesh.Indices.Length/3,VisualTriangles=selected.Count/3,CollisionTriangles=mesh.Active&&mesh.Physical?mesh.Indices.Length/3:0});
            },includeInactive:fullCourse);
        }
        if(fullCourse)
        foreach(var (section,values) in models.Where(x=>x.Key.StartsWith("DYNAMIC_OBJECT_",StringComparison.Ordinal)))
        {
            if(values["POS_MODE"]!="RANDOM" || values["MULT"]!="1,1")throw new InvalidDataException("Unsupported dynamic placement: "+section);
            var centre=Vector(values["RND_POS_CENTER"]);var range=Vector(values["RND_POS_RANGE"]);
            // Reproduce both sky assets at fixed, bounded source positions for the
            // desktop proof. AC's random spawn, probability and motion are not ported.
            float side=section.EndsWith("0",StringComparison.Ordinal)?-1:1;
            var offset=new Vector3(side*range.X*.25f,side*range.Y*.25f,-side*range.Z*.25f);
            Kn5.Read(Input(values["FILE"]),Matrix4x4.CreateTranslation(centre+offset-scene.Origin),scene.Textures,mesh=>
            {
                if(mesh.Physical)throw new InvalidDataException("Dynamic source has unexpected collision.");
                if(mesh.Visible)scene.Visuals.Add(mesh);
                scene.SourceMeshes.Add(new{File=values["FILE"],mesh.SourceNode,mesh.Name,mesh.Visible,mesh.Physical,mesh.LodIn,mesh.LodOut,SourceTriangles=mesh.Indices.Length/3,VisualTriangles=mesh.Visible?mesh.Indices.Length/3:0,CollisionTriangles=0,Placement="Static desktop sky proxy",Position=new[]{centre.X+offset.X,centre.Y+offset.Y,centre.Z+offset.Z}});
            });
        }
        if (scene.Visuals.Count==0 || scene.Collision.Count==0) throw new InvalidDataException("No source geometry selected.");
        return scene;
    }
    internal static Mesh Compact(Mesh mesh, IReadOnlyList<ushort> selected)
    {
        var used=selected.Distinct().Order().ToArray();var map=used.Select((old,index)=>(old,index)).ToDictionary(x=>x.old,x=>checked((ushort)x.index));
        return mesh with { Positions=used.Select(i=>mesh.Positions[i]).ToArray(),Normals=used.Select(i=>mesh.Normals[i]).ToArray(),UV=used.Select(i=>mesh.UV[i]).ToArray(),Tangents=used.Select(i=>mesh.Tangents[i]).ToArray(),Indices=selected.Select(i=>map[i]).ToArray(),
            Colors=mesh.Colors is null?null:used.Select(i=>mesh.Colors[i]).ToArray(),
            TexCoords=mesh.TexCoords?.Select(set=>used.Select(i=>set[i]).ToArray()).ToArray(),
            Binormals=mesh.Binormals is null?null:used.Select(i=>mesh.Binormals[i]).ToArray() };
    }
    internal float RoadHeight(Vector3 position)
    {
        float? closest=null;
        foreach(var triangle in Collision.Where(t=>RoadSurfaces.Contains(t.Material)))
        {
            var a=triangle.Position0;var b=triangle.Position1;var c=triangle.Position2;
            float divisor=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);
            if(MathF.Abs(divisor)<.000001f)continue;
            float u=((b.Z-c.Z)*(position.X-c.X)+(c.X-b.X)*(position.Z-c.Z))/divisor;
            float v=((c.Z-a.Z)*(position.X-c.X)+(a.X-c.X)*(position.Z-c.Z))/divisor;
            float w=1-u-v;
            if(MathF.Min(u,MathF.Min(v,w))<-.00001f)continue;
            float height=u*a.Y+v*b.Y+w*c.Y;
            if(MathF.Abs(height-position.Y)>4)continue;
            if(closest is null || MathF.Abs(height-position.Y)<MathF.Abs(closest.Value-position.Y))closest=height;
        }
        return closest ?? throw new InvalidDataException("No original road collision under the starting grid.");
    }
    internal Gate BeforeStart(float metres)
    {
        float distance = Length - metres;
        int index = Array.FindLastIndex(Gates, g => g.Distance <= distance);
        if (!FullCourse || index < 0) throw new InvalidDataException("Missing full-course starting approach.");
        var a = Gates[index];
        var b = index + 1 < Gates.Length ? Gates[index + 1] : Gates[0] with { Distance = Length };
        float at = (distance - a.Distance) / (b.Distance - a.Distance);
        return new(Vector3.Lerp(a.Position, b.Position, at), Vector3.Normalize(Vector3.Lerp(a.Tangent, b.Tangent, at)),
            a.Left + (b.Left - a.Left) * at, a.Right + (b.Right - a.Right) * at, -metres);
    }
    internal static string Surface(string name)
    {
        string key=Regex.Replace(name,"^[0-9]+","").ToUpperInvariant();
        if (key.StartsWith("TRM") || key.StartsWith("ROAD") || key.StartsWith("ASPH") || key.StartsWith("PITS") || key.StartsWith("PTRMBL") || key.StartsWith("PAINT")) return "RDT+";
        if (key.StartsWith("CURB") || key.StartsWith("CRB") || key.StartsWith("CNC") || key.StartsWith("CONCRETE") || key.StartsWith("CUTKRB") || key.StartsWith("WALL") || key.StartsWith("3DIMP") || key.StartsWith("3DMISC")) return "CON+";
        if (key.StartsWith("GRASS") || key.StartsWith("CUTGRA") || key.StartsWith("CARPET") || key.StartsWith("OUT")) return "GRS+";
        if (key.StartsWith("GRAVEL")) return "DRT+";
        if (key.StartsWith("SAND")) return "SND+";
        throw new InvalidDataException("Unmapped collision surface: "+name);
    }
    internal static Dictionary<string,Dictionary<string,string>> Ini(string text)
    {
        var result=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal); Dictionary<string,string>? current=null;
        foreach(var raw in text.Split('\n'))
        {
            string line=raw.Trim();if(line.Length==0 || line.StartsWith(';') || line.StartsWith('#'))continue;
            if(line.StartsWith('[') && line.EndsWith(']')) {current=new(StringComparer.Ordinal);result.Add(line[1..^1],current);continue;}
            int index=line.IndexOf('=');if(index>0 && current is not null)current.Add(line[..index].Trim(),line[(index+1)..].Trim());
        }
        return result;
    }
    static Vector3 Vector(string text)
    {
        var p=text.Split(',').Select(x=>float.Parse(x,CultureInfo.InvariantCulture)).ToArray();
        if(p.Length!=3 || p.Any(x=>!float.IsFinite(x)))throw new InvalidDataException("Invalid model placement.");return new(p[0],p[1],p[2]);
    }
}
