using System.Numerics;
using DiRT2VR.Nordschleife;

internal sealed class MistyMeshAttributes
{
    internal Mesh Apply(Mesh mesh)
    {
        if(mesh.Material.Native is null||mesh.Material.Name=="road_asphalt")return mesh;
        float alpha=MistyMaterials.Opacity(mesh.Material);
        var colors=Enumerable.Repeat(new Vector4(1,1,1,alpha),mesh.Positions.Length).ToArray();
        var result=mesh with{Colors=colors};
        if(mesh.Material.Shader=="ksPerPixelNM_UVMult"&&mesh.Material.Name!="lake1")
        {
            float diffuse=mesh.Material.Properties["diffuseMult"][0],normal=mesh.Material.Properties["normalMult"][0];
            if(diffuse<=0||MathF.Abs(diffuse-normal)>.00001f)throw new InvalidDataException("Rock normal/diffuse UV scales need separate native channels.");
            result=result with{UV=mesh.UV.Select(uv=>uv*diffuse).ToArray()};
        }
        if(mesh.Material.Name is "waterfall" or "waterfall2" or "beachwaves")result=result with{UV=mesh.UV.Select(uv=>new Vector2(uv.X,1-uv.Y)).ToArray()};
        return result;
    }
}
