using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using EgoEngineLibrary.Graphics.Dds;

namespace DiRT2VR.CustomCars;

internal static class FerrariTextureMipMaps
{
    internal static bool AddMissing(DdsFile texture)
    {
        if (texture.header.mipMapCount > 1 || (texture.header.width == 1 && texture.header.height == 1)) return false;
        var format = texture.header.ddspf.fourCC switch
        {
            0 => CompressionFormat.Bgra,
            0x31545844 => CompressionFormat.Bc1WithAlpha,
            0x33545844 => CompressionFormat.Bc2,
            0x35545844 => CompressionFormat.Bc3,
            _ => throw new InvalidDataException("Unsupported cockpit mipmap format.")
        };
        using var source = new MemoryStream();
        texture.Write(source);
        var (pixels, width, height) = DetailBake.Decode(source.ToArray());
        var bgra = new byte[checked(width * height * 4)];
        for (int i = 0; i < pixels.Length; i++)
        {
            bgra[i * 4] = pixels[i].b; bgra[i * 4 + 1] = pixels[i].g;
            bgra[i * 4 + 2] = pixels[i].r; bgra[i * 4 + 3] = pixels[i].a;
        }
        int baseBytes = format == CompressionFormat.Bgra ? bgra.Length :
            checked(((width + 3) / 4) * ((height + 3) / 4) * (format == CompressionFormat.Bc1WithAlpha ? 8 : 16));
        if (texture.bdata.Length != baseBytes) throw new InvalidDataException("Unexpected single-level cockpit texture payload.");
        var encoder = new BcEncoder(format);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        using var encoded = new MemoryStream();
        encoder.EncodeToDds(bgra, width, height, PixelFormat.Bgra32).Write(encoded);
        encoded.Position = 0;
        var mipmapped = new DdsFile(encoded);
        // Preserve the original full-resolution bytes, including alpha. Only
        // append filtered smaller levels; do not recompress the source artwork.
        texture.bdata.CopyTo(mipmapped.bdata, 0);
        texture.bdata = mipmapped.bdata;
        texture.header.mipMapCount = mipmapped.header.mipMapCount;
        texture.header.flags |= DdsHeader.Flags.DDSD_MIPMAPCOUNT;
        texture.header.caps |= DdsHeader.Caps.DDSCAPS_COMPLEX | DdsHeader.Caps.DDSCAPS_MIPMAP;
        return true;
    }
}
