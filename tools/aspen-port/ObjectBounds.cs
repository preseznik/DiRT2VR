using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

// Diagnostic only: test whether entity-parent placeholder bounds cause
// missing objects. The gate-23 experiment did not restore the barriers.
internal static class ObjectBounds
{
    static float[] Values(string text)=>text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
    internal static int Rebuild(XDocument doc)
    {
        int changed=0;
        foreach(var node in doc.Descendants().Where(n=>n.Element("BOUNDINGBOX") is not null).Reverse().ToArray())
        {
            var box=node.Element("BOUNDINGBOX")!;var b=Values(box.Value);
            if(b.Length!=6 || b.Any(v=>!float.IsFinite(v)) || Enumerable.Range(0,3).Any(i=>b[i]>b[i+3])) throw new InvalidDataException("Invalid object bounds.");
            var min=new Vector3(b[0],b[1],b[2]);var max=new Vector3(b[3],b[4],b[5]);
            foreach(var child in node.Elements().Where(n=>n.Element("BOUNDINGBOX") is not null))
            {
                var c=Values(child.Element("BOUNDINGBOX")!.Value);var f=Values(child.Element("TRANSFORM")?.Value ?? throw new InvalidDataException("Missing object transform."));
                if(f.Length!=16 || f.Any(v=>!float.IsFinite(v))) throw new InvalidDataException("Invalid object transform.");
                var m=new Matrix4x4(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]);
                if(m.M14!=0 || m.M24!=0 || m.M34!=0 || m.M44!=1) throw new InvalidDataException("Non-affine object transform.");
                for(int corner=0;corner<8;corner++) {
                    var p=Vector3.Transform(new Vector3(c[(corner&1)==0?0:3],c[(corner&2)==0?1:4],c[(corner&4)==0?2:5]),m);
                    if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)) throw new InvalidDataException("Nonfinite object bounds.");
                    min=Vector3.Min(min,p);max=Vector3.Max(max,p);
                }
            }
            float[] updated=[min.X,min.Y,min.Z,max.X,max.Y,max.Z];
            if(!updated.SequenceEqual(b)) { box.Value=string.Join(' ',updated.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));changed++; }
        }
        return changed;
    }
}
