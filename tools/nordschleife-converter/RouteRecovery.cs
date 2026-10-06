using System.Numerics;
using EgoEngineLibrary.Formats.TrackQuadTree;

namespace DiRT2VR.Nordschleife;

// A wide recovery strip can overlap another leg of a climbing hairpin.
// Keep its floor below every nearby original road face, including that other leg.
internal sealed class RouteRecovery
{
    const float Clearance=6;
    readonly Dictionary<Cell,List<QuadTreeDataTriangle>> road=[];
    static Cell Tile(Vector3 p)=>new((int)MathF.Floor(p.X/64),(int)MathF.Floor(p.Z/64));
    internal RouteRecovery(IEnumerable<QuadTreeDataTriangle> collision)
    {
        foreach(var t in collision.Where(t=>t.Material=="RDT+"))
        {
            var min=Tile(Vector3.Min(t.Position0,Vector3.Min(t.Position1,t.Position2)));
            var max=Tile(Vector3.Max(t.Position0,Vector3.Max(t.Position1,t.Position2)));
            for(int x=min.X;x<=max.X;x++)for(int z=min.Z;z<=max.Z;z++)
            {
                var cell=new Cell(x,z);if(!road.TryGetValue(cell,out var list))road[cell]=list=[];list.Add(t);
            }
        }
    }
    internal float Floor(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
    {
        var min=Vector3.Min(Vector3.Min(a,b),Vector3.Min(c,d));
        var max=Vector3.Max(Vector3.Max(a,b),Vector3.Max(c,d));
        float height=min.Y-Clearance;var first=Tile(min);var last=Tile(max);
        for(int x=first.X;x<=last.X;x++)for(int z=first.Z;z<=last.Z;z++)
        {
            if(!road.TryGetValue(new(x,z),out var triangles))continue;
            foreach(var t in triangles)
            {
                var low=Vector3.Min(t.Position0,Vector3.Min(t.Position1,t.Position2));
                var high=Vector3.Max(t.Position0,Vector3.Max(t.Position1,t.Position2));
                if(high.X>=min.X&&low.X<=max.X&&high.Z>=min.Z&&low.Z<=max.Z)height=MathF.Min(height,low.Y-Clearance);
            }
        }
        return height;
    }
}
