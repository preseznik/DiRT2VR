using System.Numerics;
using EgoEngineLibrary.Formats.TrackQuadTree;

namespace DiRT2VR.Nordschleife;

internal static class GeometryCoverage
{
    const float CellSize=.04f;
    static (int X,int Y,int Z) Cell(Vector3 p)=>((int)MathF.Floor(p.X/CellSize),(int)MathF.Floor(p.Y/CellSize),(int)MathF.Floor(p.Z/CellSize));
    internal static int Unmatched(IReadOnlyList<QuadTreeDataTriangle> source,IReadOnlyList<QuadTreeDataTriangle> target)
    {
        var index=new Dictionary<(int,int,int,string),List<int>>();
        for(int i=0;i<target.Count;i++)foreach(var p in new[]{target[i].Position0,target[i].Position1,target[i].Position2})
        {
            var c=Cell(p);var key=(c.X,c.Y,c.Z,target[i].Material);
            if(!index.TryGetValue(key,out var values))index[key]=values=[];values.Add(i);
        }
        bool Near(Vector3 a,Vector3 b)=>Vector3.DistanceSquared(a,b)<=.02f*.02f;
        bool Same(QuadTreeDataTriangle a,QuadTreeDataTriangle b)=>
            Near(a.Position0,b.Position0)&&Near(a.Position1,b.Position1)&&Near(a.Position2,b.Position2)||
            Near(a.Position0,b.Position1)&&Near(a.Position1,b.Position2)&&Near(a.Position2,b.Position0)||
            Near(a.Position0,b.Position2)&&Near(a.Position1,b.Position0)&&Near(a.Position2,b.Position1);
        int unmatched=0;
        foreach(var t in source)
        {
            var c=Cell(t.Position0);bool found=false;
            for(int x=-1;x<=1&&!found;x++)for(int y=-1;y<=1&&!found;y++)for(int z=-1;z<=1&&!found;z++)
                if(index.TryGetValue((c.X+x,c.Y+y,c.Z+z,t.Material),out var candidates))
                    foreach(int i in candidates)if(Same(t,target[i])){found=true;break;}
            if(!found)unmatched++;
        }
        return unmatched;
    }
    internal readonly record struct ExactFace(Vector3 A,Vector3 B,Vector3 C,string Material);
    internal static ExactFace Exact(QuadTreeDataTriangle t)
    {
        var a=t.Position0;var b=t.Position1;var c=t.Position2;
        static int Compare(Vector3 a,Vector3 b){int cmp=a.X.CompareTo(b.X);if(cmp!=0)return cmp;cmp=a.Y.CompareTo(b.Y);return cmp!=0?cmp:a.Z.CompareTo(b.Z);}
        if(Compare(b,a)<0&&Compare(b,c)<=0)return new(b,c,a,t.Material);
        if(Compare(c,a)<0&&Compare(c,b)<0)return new(c,a,b,t.Material);
        return new(a,b,c,t.Material);
    }
}
