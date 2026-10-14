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
    // DX11 terrain reads diffuse alpha as specular strength, not opacity. Its
    // shared TrackSpecularParameters ignore the legacy material PhongScale.
    internal static void PackDetail(DdsFile file)
    {
        uint format=file.header.ddspf.fourCC;
        if(format==0x31545844) // BC1 has implicit alpha one; add explicit BC3 alpha.
        {
            if(file.bdata.Length%8!=0)throw new InvalidDataException("Incomplete asphalt BC1 block.");
            var blocks=new byte[checked(file.bdata.Length*2)];
            for(int i=0;i<file.bdata.Length;i+=8)
            {
                var block=file.bdata.AsSpan(i,8);
                if(BinaryPrimitives.ReadUInt16LittleEndian(block)<=BinaryPrimitives.ReadUInt16LittleEndian(block[2..]))
                    throw new InvalidDataException("Three-colour asphalt BC1 cannot be copied losslessly to BC3.");
                block.CopyTo(blocks.AsSpan(i*2+8,8));
            }
            file.bdata=blocks;file.header.ddspf.fourCC=0x35545844;
            file.header.pitchOrLinearSize=((file.header.width+3)/4)*((file.header.height+3)/4)*16;
            return;
        }
        if(format is not (0x33545844 or 0x35545844)||file.bdata.Length%16!=0)
            throw new InvalidDataException("Unsupported asphalt detail DDS layout.");
        // Preserve every compressed RGB block and all authored mip levels.
        for(int i=0;i<file.bdata.Length;i+=16)file.bdata.AsSpan(i,8).Clear();
    }
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
