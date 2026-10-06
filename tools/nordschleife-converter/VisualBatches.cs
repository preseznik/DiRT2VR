namespace DiRT2VR.Nordschleife;

// Consolidate static draws without changing triangles, attributes or LOD intervals.
// Retain individual world-detail ground meshes so their half-UV repeat origin stays local.
internal static class VisualBatches
{
    internal static IEnumerable<Mesh> Combine(IEnumerable<Mesh> meshes)
    {
        foreach(var group in meshes.GroupBy(m=>(m.Material.Id,m.LodIn,m.LodOut)))
        {
            var batch=new List<Mesh>();int vertices=0;
            foreach(var mesh in group)
            {
                if(GrassMaterial.Read(mesh.Material) is not null){yield return mesh;continue;}
                if(vertices+mesh.Positions.Length>ushort.MaxValue&&batch.Count>0)
                {
                    yield return Merge(batch);batch.Clear();vertices=0;
                }
                batch.Add(mesh);vertices+=mesh.Positions.Length;
            }
            if(batch.Count>0)yield return Merge(batch);
        }
    }
    static Mesh Merge(List<Mesh> meshes)
    {
        if(meshes.Count==1)return meshes[0];
        var indices=new List<ushort>();int offset=0;
        foreach(var mesh in meshes)
        {
            indices.AddRange(mesh.Indices.Select(i=>checked((ushort)(i+offset))));offset+=mesh.Positions.Length;
        }
        return meshes[0] with{Name=meshes[0].Name+"_batch",Positions=meshes.SelectMany(m=>m.Positions).ToArray(),
            Normals=meshes.SelectMany(m=>m.Normals).ToArray(),UV=meshes.SelectMany(m=>m.UV).ToArray(),
            Tangents=meshes.SelectMany(m=>m.Tangents).ToArray(),Indices=indices.ToArray()};
    }
}
