using System.Numerics;

namespace DiRT2VR.Nordschleife;

internal static class Tiles
{
    internal const int NativeLimit=200;
    internal static (List<List<Mesh>> Parts,int Size) Partition(IEnumerable<Mesh> meshes,int? fixedCount=null)
    {
        int size=128;Dictionary<Cell,List<Mesh>> partition;
        while(true)
        {
            partition=[];
            foreach(var mesh in meshes)
            {
                var buckets=new Dictionary<Cell,List<ushort>>();
                for(int i=0;i<mesh.Indices.Length;i+=3)
                {
                    var centre=(mesh.Positions[mesh.Indices[i]]+mesh.Positions[mesh.Indices[i+1]]+mesh.Positions[mesh.Indices[i+2]])/3;
                    var cell=new Cell((int)MathF.Floor(centre.X/size),(int)MathF.Floor(centre.Z/size));
                    if(!buckets.TryGetValue(cell,out var indices))buckets[cell]=indices=[];
                    indices.AddRange([mesh.Indices[i],mesh.Indices[i+1],mesh.Indices[i+2]]);
                }
                foreach(var (cell,indices) in buckets)
                {
                    if(!partition.TryGetValue(cell,out var list))partition[cell]=list=[];
                    list.Add(Scene.Compact(mesh,indices));
                }
            }
            if(partition.Count<=(fixedCount??NativeLimit))break;
            size*=2;if(size>16384)throw new InvalidDataException("Scene cannot fit the native terrain tile capacity.");
        }
        var parts=partition.OrderBy(p=>p.Key.X).ThenBy(p=>p.Key.Z).Select(p=>p.Value).ToList();
        if(parts.Count==0 || parts.Count>NativeLimit)throw new InvalidDataException("Unsupported spatial tile count.");
        while(fixedCount is not null && parts.Count<fixedCount)
        {
            int part=Enumerable.Range(0,parts.Count).OrderByDescending(i=>parts[i].Sum(m=>m.Indices.Length)).First();
            var faces=parts[part].SelectMany(m=>Enumerable.Range(0,m.Indices.Length/3).Select(i=>
                (Mesh:m,At:i*3,Centre:(m.Positions[m.Indices[i*3]]+m.Positions[m.Indices[i*3+1]]+m.Positions[m.Indices[i*3+2]])/3))).ToArray();
            if(faces.Length<2)throw new InvalidDataException("Not enough geometry for the donor tile inventory.");
            var min=faces.Select(f=>f.Centre).Aggregate(Vector3.Min);var max=faces.Select(f=>f.Centre).Aggregate(Vector3.Max);
            int axis=max.X-min.X>=max.Z-min.Z?0:2;faces=faces.OrderBy(f=>f.Centre[axis]).ToArray();
            List<Mesh> Half(int start,int count)=>faces.Skip(start).Take(count).GroupBy(f=>f.Mesh).Select(g=>Scene.Compact(g.Key,g.SelectMany(f=>g.Key.Indices.AsSpan(f.At,3).ToArray()).ToArray())).ToList();
            int middle=faces.Length/2;parts[part]=Half(0,middle);parts.Insert(part+1,Half(middle,faces.Length-middle));
        }
        return(parts,size);
    }
}
