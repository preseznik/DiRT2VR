using System.Numerics;
using System.Buffers.Binary;
using EgoEngineLibrary.Graphics.Dds;

namespace DiRT2VR.Nordschleife;

// Multilayer asphalt samples its grain in original world XZ. The base map alone
// contains broad shading, repairs and markings, not the final road colour.
internal sealed record RoadMaterial(string Detail, string Shading, float Scale)
{
    internal static RoadMaterial? Read(Material material)
    {
        if (material.Blend!=0 || material.AlphaTest || !material.Shader.StartsWith("ksMultilayer", StringComparison.Ordinal) ||
            !material.Textures.TryGetValue("txDiffuse", out var shading) ||
            !shading.StartsWith("asph", StringComparison.OrdinalIgnoreCase) ||
            !material.Textures.TryGetValue("txDetailR", out var detail)) return null;
        if (!material.Properties.TryGetValue("multR", out var property) || property.Length != 10 ||
            !float.IsFinite(property[0]) || property[0] <= 0 || property[0] > 10)
            throw new InvalidDataException("Invalid asphalt detail scale: " + material.Id);
        return new(detail, shading, property[0]);
    }
    internal Vector4 Mapping(Vector3 origin) => new(1 / Scale, -1 / Scale, origin.X * Scale, 1 - origin.Z * Scale);
    internal static void PackShading(DdsFile file)
    {
        if(file.header.ddspf.fourCC is not (0x33545844 or 0x35545844) || file.bdata.Length%16!=0)
            throw new InvalidDataException("Unsupported asphalt shading DDS layout.");
        // EGO's mask uses red/green for occlusion and blue for diffuse shading.
        // Neutralize occlusion without darkening the source shading twice. DXT3/5
        // always interpolate four colours, so these edits retain every blue sample.
        for(int i=0;i<file.bdata.Length;i+=16)
        {
            for(int j=8;j<=10;j+=2)
            {
                var endpoint=file.bdata.AsSpan(i+j,2);
                BinaryPrimitives.WriteUInt16LittleEndian(endpoint,(ushort)(BinaryPrimitives.ReadUInt16LittleEndian(endpoint)|0xffe0));
            }
        }
        DdsAlpha.Opaque(file);
    }
}
