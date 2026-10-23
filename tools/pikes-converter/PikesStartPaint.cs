using System.Numerics;
using DiRT2VR.Nordschleife;

internal static class PikesStartPaint
{
    internal static void Apply(Scene scene,string output)
    {
        var road=new List<(Vector3 A,Vector3 B,Vector3 C)>();
        foreach(var mesh in scene.Visuals.Where(m=>m.Material.Name.StartsWith("trk ",StringComparison.Ordinal)))
        for(int k=0;k<mesh.Indices.Length;k+=3)
        {
            var a=mesh.Positions[mesh.Indices[k]];var b=mesh.Positions[mesh.Indices[k+1]];var c=mesh.Positions[mesh.Indices[k+2]];
            if(new[]{a,b,c}.All(p=>MathF.Abs(p.X)<20&&p.Z>-15&&p.Z<5))road.Add((a,b,c));
        }
        // Historic gravel has no painted tarmac start line.
        var paint=scene.Visuals.Where(m=>m.Material.Name.StartsWith("decal_lines!",StringComparison.Ordinal)).ToArray();
        if(paint.Length==0)return;
        if(road.Count==0)throw new InvalidDataException("START paint has no source road surface");
        float? Ground(Vector3 p)=>road.Select(t=>Height(p,t.A,t.B,t.C)).Where(y=>y.HasValue).OrderBy(y=>MathF.Abs(y!.Value-p.Y)).FirstOrDefault();
        int changed=0,clipped=0;float min=float.MaxValue,max=float.MinValue;
        foreach(var mesh in scene.Visuals.Where(m=>m.Material.Name.StartsWith("decal_lines!",StringComparison.Ordinal)||m.Material.Name.StartsWith("decal_damage!",StringComparison.Ordinal)))
        {
            var selected=new HashSet<int>();
            for(int k=0;k<mesh.Indices.Length;k+=3)
            {
                int[] ids=[mesh.Indices[k],mesh.Indices[k+1],mesh.Indices[k+2]];var points=ids.Select(i=>mesh.Positions[i]).ToArray();
                bool transverse=!mesh.Material.Name.StartsWith("decal_lines!",StringComparison.Ordinal)||points.Max(p=>p.X)-points.Min(p=>p.X)>.7f&&points.Max(p=>p.Z)-points.Min(p=>p.Z)<.5f;
                if(transverse&&points.All(p=>MathF.Abs(p.X)<7&&p.Z>-8&&p.Z< -4))selected.UnionWith(ids);
            }
            foreach(int i in selected)
            {
                var p=mesh.Positions[i];var q=p;var y=Ground(q);
                if(y is null)
                {
                    var options=new List<(Vector3 P,float Distance)>();
                    foreach(var t in road)foreach(var edge in new[]{(t.A,t.B),(t.B,t.C),(t.C,t.A)})
                    {
                        var a=new Vector2(edge.Item1.X,edge.Item1.Z);var b=new Vector2(edge.Item2.X,edge.Item2.Z);var v=new Vector2(p.X,p.Z);
                        float f=Math.Clamp(Vector2.Dot(v-a,b-a)/Vector2.DistanceSquared(a,b),0,1);if(!float.IsFinite(f))continue;
                        var on=Vector3.Lerp(edge.Item1,edge.Item2,f);var toward=(t.A+t.B+t.C)/3-on;toward.Y=0;if(toward.LengthSquared()<1e-8f)continue;
                        on+=Vector3.Normalize(toward)*.01f;if(Ground(on) is not null)options.Add((on,Vector2.DistanceSquared(new(on.X,on.Z),v)));
                    }
                    if(options.Count==0)throw new InvalidDataException("Missing asphalt beneath START paint");
                    q=options.MinBy(o=>o.Distance).P;
                    if(Vector2.Distance(new(q.X,q.Z),new(p.X,p.Z))>.5f)throw new InvalidDataException("Unexpected START edge correction");
                    y=Ground(q);clipped++;
                }
                q.Y=y!.Value+.005f;mesh.Positions[i]=q;changed++;
            }
            for(int k=0;k<mesh.Indices.Length;k+=3)
            {
                int[] ids=[mesh.Indices[k],mesh.Indices[k+1],mesh.Indices[k+2]];if(!ids.All(selected.Contains))continue;
                var a=mesh.Positions[ids[0]];var b=mesh.Positions[ids[1]];var c=mesh.Positions[ids[2]];
                for(int u=0;u<=4;u++)for(int v=0;v<=4-u;v++)
                {
                    var p=a*(u/4f)+b*(v/4f)+c*(1-(u+v)/4f);var y=Ground(p);
                    if(y is null)throw new InvalidDataException("START triangle overhangs road");
                    min=MathF.Min(min,p.Y-y.Value);max=MathF.Max(max,p.Y-y.Value);
                }
            }
        }
        if(changed is <48 or >54||clipped is <8 or >10||min<.001f||max>.009f)throw new InvalidDataException($"START surface check: {changed} vertices, {clipped} endpoints, {min}..{max} clearance");
        Files.Json(Path.Combine(output,"start-paint.json"),new{Vertices=changed,ClippedEndpoints=clipped,MinClearanceMetres=min,MaxClearanceMetres=max});
    }
    static float? Height(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        float d=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);if(MathF.Abs(d)<1e-6f)return null;
        float u=((b.Z-c.Z)*(p.X-c.X)+(c.X-b.X)*(p.Z-c.Z))/d,v=((c.Z-a.Z)*(p.X-c.X)+(a.X-c.X)*(p.Z-c.Z))/d;
        return u>=-.001f&&v>=-.001f&&u+v<=1.001f?u*a.Y+v*b.Y+(1-u-v)*c.Y:null;
    }
}
