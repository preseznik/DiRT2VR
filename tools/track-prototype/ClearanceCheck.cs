using System.Numerics;
using System.Text.Json;

internal static class ClearanceCheck
{
    internal static void Run(string specPath,string collisionPath,string output)
    {
        if(File.Exists(output)) throw new IOException("Output must be new.");
        var spec=JsonSerializer.Deserialize<TrackAuthor.Spec>(File.ReadAllText(specPath))!;
        var triangles=GeometryCheck.Read(collisionPath);
        var hits=new List<object>();
        for(int i=0;i<spec.points.Length;i++)
        {
            Vector3 Point(int k,float side,float up)
            {
                var g=spec.points[k%spec.points.Length];
                return new Vector3(g.position[0]+g.tangent[2]*side,g.position[1]+up,g.position[2]-g.tangent[0]*side);
            }
            foreach(var side in new[]{-4f,0,4f})
            foreach(var height in new[]{0.3f,1.2f,2f})
            {
                var a=Point(i,side,height); var b=Point(i+1,side,height);
                foreach(var t in triangles)
                {
                    // Moller-Trumbore segment/triangle test; both face directions count.
                    var d=b-a; var e1=t.B-t.A;var e2=t.C-t.A;var p=Vector3.Cross(d,e2);var det=Vector3.Dot(e1,p);
                    if(Math.Abs(det)<1e-7) continue;
                    var s=a-t.A;var u=Vector3.Dot(s,p)/det; if(u<0 || u>1) continue;
                    var q=Vector3.Cross(s,e1);var v=Vector3.Dot(d,q)/det; if(v<0 || u+v>1) continue;
                    var distance=Vector3.Dot(e2,q)/det;if(distance<0 || distance>1) continue;
                    hits.Add(new { Gate=i, Side=side, Height=height, Object=t.Material });
                }
            }
        }
        File.WriteAllText(output,JsonSerializer.Serialize(new { Source=Path.GetFullPath(collisionPath), SampledIntersections=hits.Count, Hits=hits, CompleteVehicleSweep=false },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Retained object collision intersections: {hits.Count}");
    }
}
