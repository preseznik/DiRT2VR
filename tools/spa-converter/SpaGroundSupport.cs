using System.Numerics;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// GRID 2's road quadtree omits much of the visible infield behind its fences.
// Retain existing contacts and fill only unsupported original terrain faces.
internal static class SpaGroundSupport
{
    internal sealed class Surface
    {
        readonly Dictionary<(int,int),List<QuadTreeDataTriangle>> cells=[];
        static int Cell(float n)=>(int)MathF.Floor(n/20);
        internal Surface(IEnumerable<QuadTreeDataTriangle> triangles)
        {
            foreach(var t in triangles){
                var min=Vector3.Min(t.Position0,Vector3.Min(t.Position1,t.Position2));var max=Vector3.Max(t.Position0,Vector3.Max(t.Position1,t.Position2));
                for(int x=Cell(min.X);x<=Cell(max.X);x++)for(int z=Cell(min.Z);z<=Cell(max.Z);z++){
                    if(!cells.TryGetValue((x,z),out var list))cells[(x,z)]=list=[];list.Add(t);
                }
            }
        }
        internal float? Height(Vector3 p)
        {
            float? result=null;
            if(cells.TryGetValue((Cell(p.X),Cell(p.Z)),out var faces))foreach(var t in faces){
                var a=t.Position0;var b=t.Position1;var c=t.Position2;float den=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);
                if(MathF.Abs(den)<1e-8f)continue;
                float u=((b.Z-c.Z)*(p.X-c.X)+(c.X-b.X)*(p.Z-c.Z))/den;
                float v=((c.Z-a.Z)*(p.X-c.X)+(a.X-c.X)*(p.Z-c.Z))/den;
                if(u<-.00001f||v<-.00001f||u+v>1.00001f)continue;
                float y=u*a.Y+v*b.Y+(1-u-v)*c.Y;
                if(result is null||MathF.Abs(y-p.Y)<MathF.Abs(result.Value-p.Y))result=y;
            }
            return result;
        }
        internal bool Supports(Vector3 p,float tolerance=.03f)=>Height(p) is float h&&MathF.Abs(h-p.Y)<=tolerance;
    }
    internal static IEnumerable<Vector3> Probes(QuadTreeDataTriangle t)=>[
        t.Position0,t.Position1,t.Position2,(t.Position0+t.Position1)/2,(t.Position1+t.Position2)/2,(t.Position2+t.Position0)/2,(t.Position0+t.Position1+t.Position2)/3];
    internal static void Add(Scene scene,PssgFile terrain,Func<Vector3,float,bool> near,string output)
    {
        var surface=new Surface(scene.Collision.Where(t=>t.Material!="METL"));
        var added=new List<QuadTreeDataTriangle>();var coverage=new List<object>();int checkedFaces=0;
        foreach(var node in terrain.Elements<PssgRenderNode>().Where(n=>(n.Id.StartsWith("NONLOD",StringComparison.Ordinal)||n.Id.StartsWith("HIGH_",StringComparison.Ordinal)||n.Id.StartsWith("SHADOWCASTING_",StringComparison.Ordinal))&&!n.Id.Contains("BATCH",StringComparison.Ordinal)))
        foreach(var draw in node.ChildElements.OfType<PssgRenderStreamInstance>()){
            string family=draw.GetShaderInstance().GetShaderGroup().Id;
            if(family is not("terrain_infield.fx" or "terrain_track_infield.fx"))continue;
            if(node.Transform.Transform!=Matrix4x4.Identity)throw new InvalidDataException("Unexpected terrain frame");
            var r=new RenderDataSourceReader(draw.GetRenderDataSource());int count=0;
            if(r.Primitive!="triangles"||r.IndexCount%3!=0)throw new InvalidDataException("Invalid source ground topology");
            for(int i=0;i<r.IndexCount;i+=3){
                var t=new QuadTreeDataTriangle(r.GetPosition(r.GetIndex(i))-scene.Origin,r.GetPosition(r.GetIndex(i+1))-scene.Origin,r.GetPosition(r.GetIndex(i+2))-scene.Origin,"GRS*");
                // Keep source winding; vertical/down-facing scenery is not driveable ground.
                if(Vector3.Cross(t.Position1-t.Position0,t.Position2-t.Position0).Y<=1e-8f||!Probes(t).Any(p=>near(p,50)))continue;
                checkedFaces++;
                if(Probes(t).All(p=>surface.Supports(p)))continue;
                added.Add(t);count++;
            }
            if(count>0)coverage.Add(new{Draw=draw.Id,Shader=draw.GetShaderInstance().Id,Family=family,AddedFaces=count});
        }
        var unique=added.DistinctBy(GeometryCoverage.Exact).ToArray();scene.Collision.AddRange(unique);
        Files.Json(Path.Combine(output,"grass-support.json"),new{CheckedSourceFaces=checkedFaces,AddedSourceFaces=unique.Length,CorridorMetres=50,ExistingCollisionUnchanged=true,OriginalVerticesAndWindingRetained=true,Material="GRS*",Draws=coverage,RuntimeValidated=false});
        SpaCollisionInspect.Dump(Path.Combine(output,"added-grass.bin"),unique);
    }
}
