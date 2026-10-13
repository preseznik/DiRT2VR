using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using EgoEngineLibrary.Graphics.Dds;

namespace DiRT2VR.CustomCars;

internal static class FerrariNormalMap
{
    // DiRT's donor normals store tangent X in alpha and Y in green.
    // AC stores X/Y in red/green; copying its opaque alpha bends every normal.
    internal static DdsFile Pack(byte[] source)
    {
        var (pixels,width,height)=DetailBake.Decode(source);
        var bgra=new byte[checked(width*height*4)];
        for(int i=0;i<pixels.Length;i++)
        {
            var normal=pixels[i];
            bgra[i*4]=bgra[i*4+1]=bgra[i*4+2]=normal.g;
            bgra[i*4+3]=normal.r;
        }
        var encoder=new BcEncoder(CompressionFormat.Bc3);
        encoder.OutputOptions.GenerateMipMaps=true;
        encoder.OutputOptions.Quality=CompressionQuality.Balanced;
        using var encoded=new MemoryStream();
        encoder.EncodeToDds(bgra,width,height,PixelFormat.Bgra32).Write(encoded);
        encoded.Position=0;return new DdsFile(encoded);
    }
}
