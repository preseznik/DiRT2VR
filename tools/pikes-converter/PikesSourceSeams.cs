using System.Numerics;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.TrackQuadTree;

// Two original physical-mesh gaps. Clip each repair from its visible source
// triangle so the correction follows the bank instead of adding a flat floor.
internal static class PikesSourceSeams
{
    static readonly Vector3[] Targets=[new(-2628.686f,340.7804f,3250.6318f),new(-2600.0498f,346.30502f,3293.96f)];
    internal static void Apply(Scene scene,string output)
    {
        const float radius=.06f;string[] surfaces=["SDT+","HDG*"];var records=new List<object>();
        for(int index=0;index<Targets.Length;index++)
        {
            var target=Targets[index];var matches=new List<(Vector3 A,Vector3 B,Vector3 C,float Error)>();
            foreach(var mesh in scene.Visuals.Where(m=>(m.Material.Native?.Group.Attribute("id")?.Value.StartsWith("pikes_bank_",StringComparison.Ordinal)??false)||m.Material.Native?.Group.Attribute("id")?.Value=="pikes_terrain_infield_nm.fx"))
            for(int i=0;i<mesh.Indices.Length;i+=3)
            {
                var a=mesh.Positions[mesh.Indices[i]];var b=mesh.Positions[mesh.Indices[i+1]];var c=mesh.Positions[mesh.Indices[i+2]];
                if(target.X<MathF.Min(a.X,MathF.Min(b.X,c.X))||target.X>MathF.Max(a.X,MathF.Max(b.X,c.X))||target.Z<MathF.Min(a.Z,MathF.Min(b.Z,c.Z))||target.Z>MathF.Max(a.Z,MathF.Max(b.Z,c.Z)))continue;
                float d=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);if(MathF.Abs(d)<1e-7f)continue;
                float u=((b.Z-c.Z)*(target.X-c.X)+(c.X-b.X)*(target.Z-c.Z))/d,v=((c.Z-a.Z)*(target.X-c.X)+(a.X-c.X)*(target.Z-c.Z))/d;
                if(u<0||v<0||u+v>1)continue;float error=MathF.Abs(u*a.Y+v*b.Y+(1-u-v)*c.Y-target.Y);
                if(error<.001f)matches.Add((a,b,c,error));
            }
            if(matches.Count==0)throw new InvalidDataException("Original source triangle missing at ground seam "+index);
            var face=matches.MinBy(t=>t.Error);var polygon=new List<Vector3>{face.A,face.B,face.C};
            polygon=Clip(polygon,0,target.X-radius,true);polygon=Clip(polygon,0,target.X+radius,false);polygon=Clip(polygon,1,target.Z-radius,true);polygon=Clip(polygon,1,target.Z+radius,false);
            if(polygon.Count<3)throw new InvalidDataException("Empty source seam repair");
            for(int n=1;n<polygon.Count-1;n++)
            {
                var a=polygon[0];var b=polygon[n];var c=polygon[n+1];if(Vector3.Cross(b-a,c-a).Y<0)(b,c)=(c,b);
                if(Vector3.Cross(b-a,c-a).LengthSquared()<1e-12f)throw new InvalidDataException("Degenerate source seam repair");
                scene.Collision.Add(new QuadTreeDataTriangle(a,b,c,surfaces[index]));
            }
            records.Add(new{Target=index,Triangles=polygon.Count-2,SourceHeightError=face.Error,RadiusMetres=radius});
        }
        Files.Json(Path.Combine(output,"source-ground-seams.json"),records);
    }
    static List<Vector3> Clip(List<Vector3> polygon,int axis,float boundary,bool greater)
    {
        if(polygon.Count==0)return polygon;var result=new List<Vector3>();float Coordinate(Vector3 p)=>axis==0?p.X:p.Z;
        var previous=polygon[^1];bool previousInside=greater?Coordinate(previous)>=boundary:Coordinate(previous)<=boundary;
        foreach(var current in polygon)
        {
            bool inside=greater?Coordinate(current)>=boundary:Coordinate(current)<=boundary;
            if(inside!=previousInside)result.Add(Vector3.Lerp(previous,current,(boundary-Coordinate(previous))/(Coordinate(current)-Coordinate(previous))));
            if(inside)result.Add(current);previous=current;previousInside=inside;
        }
        return result;
    }
}
