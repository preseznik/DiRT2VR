using System.Numerics;

namespace DiRT2VR.Nordschleife;

internal static class ProgressGates
{
    internal const int NativeLimit=256;
    // All three point lines share a bounded native allocator. Leave room for
    // gate/route objects; this budget is deliberately below its failure point.
    internal const int PointLineBudget=768;
    internal const int AiGateBudget=1024;
    internal static Gate[] Select(Gate[] source)=>Indices(source,NativeLimit).Select(i=>source[i]).ToArray();
    internal static int[] Indices(Gate[] source,int limit,IEnumerable<int>? required=null)
    {
        if(limit<2)throw new ArgumentOutOfRangeException(nameof(limit));
        var selected=new SortedSet<int>{0,source.Length-1};
        if(required is not null)selected.UnionWith(required);
        if(selected.Any(i=>i<0||i>=source.Length)||selected.Count>limit)throw new InvalidDataException("Protected route points exceed the native budget.");
        if(source.Length<=limit)return Enumerable.Range(0,source.Length).ToArray();
        while(selected.Count<limit)
        {
            float worst=-1;int split=-1;var indices=selected.ToArray();
            for(int s=0;s+1<indices.Length;s++)
            {
                int first=indices[s],last=indices[s+1];var a=source[first].Position;var edge=source[last].Position-a;float length=edge.LengthSquared();
                for(int i=first+1;i<last;i++)
                {
                    float t=length>0?Math.Clamp(Vector3.Dot(source[i].Position-a,edge)/length,0,1):0;
                    float error=Vector3.DistanceSquared(source[i].Position,a+edge*t);
                    if(error>worst){worst=error;split=i;}
                }
            }
            if(split<0 || worst<.25f*.25f)break;
            selected.Add(split);
        }
        return selected.ToArray();
    }
}
