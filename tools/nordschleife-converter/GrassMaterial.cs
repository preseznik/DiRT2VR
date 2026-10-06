using System.Numerics;

namespace DiRT2VR.Nordschleife;

// AC's multilayer grass diffuse is a pale shading map. Its colour/detail is sampled
// in source world XZ, with the material's multR/multG scale. Preserve that pairing.
internal sealed record GrassMaterial(string Detail,string Shading,float Scale)
{
    internal static GrassMaterial? Read(Material material)
    {
        if(!material.Shader.StartsWith("ksMultilayer",StringComparison.Ordinal)||
           !material.Textures.TryGetValue("txDiffuse",out var shading)||
           !shading.StartsWith("grass",StringComparison.OrdinalIgnoreCase))return null;
        foreach(string channel in new[]{"R","G"})
        {
            if(!material.Textures.TryGetValue("txDetail"+channel,out var detail)||!detail.Contains("grass",StringComparison.OrdinalIgnoreCase))continue;
            if(!material.Properties.TryGetValue("mult"+channel,out var property)||property.Length!=10||!float.IsFinite(property[0])||property[0]<=0||property[0]>10)
                throw new InvalidDataException("Invalid grass detail scale: "+material.Id);
            return new(detail,shading,property[0]);
        }
        throw new InvalidDataException("Grass material has no supported detail layer: "+material.Id);
    }
    internal Vector2 UV(Vector3 position,Vector3 origin,Vector3 anchor)
    {
        // Subtract one integer repeat per mesh, rather than wrapping individual
        // vertices: this preserves interpolation while improving half-float precision.
        var phase=new Vector2(anchor.X+origin.X,anchor.Z+origin.Z)*Scale;
        phase-=new Vector2(MathF.Floor(phase.X),MathF.Floor(phase.Y));
        return new Vector2(position.X-anchor.X,position.Z-anchor.Z)*Scale+phase;
    }
}
