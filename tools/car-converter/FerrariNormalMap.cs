using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using EgoEngineLibrary.Graphics.Dds;

namespace DiRT2VR.CustomCars;

internal static class FerrariNormalMap
{
    // Exterior car shaders read tangent X/Y from alpha/green; interior.fx reads RGB.
    // Reconstruct positive Z from AC's red/green for the cockpit shader. Feeding
    // it the exterior packing produces near-zero normals on otherwise flat surfaces.
    internal static DdsFile Pack(byte[] source, bool interior)
    {
        var (pixels,width,height)=DetailBake.Decode(source);
        var bgra=new byte[checked(width*height*4)];
        for(int i=0;i<pixels.Length;i++)
        {
            var normal=pixels[i];
            if(interior)
            {
                float x=normal.r/127.5f-1,y=normal.g/127.5f-1;
                float z=MathF.Sqrt(MathF.Max(0,1-x*x-y*y));
                float length=MathF.Sqrt(x*x+y*y+z*z);
                bgra[i*4]=Encode(z/length);
                bgra[i*4+1]=Encode(y/length);
                bgra[i*4+2]=Encode(x/length);
                bgra[i*4+3]=255;
            }
            else
            {
                bgra[i*4]=bgra[i*4+1]=bgra[i*4+2]=normal.g;
                bgra[i*4+3]=normal.r;
            }
        }
        var encoder=new BcEncoder(CompressionFormat.Bc3);
        encoder.OutputOptions.GenerateMipMaps=true;
        encoder.OutputOptions.Quality=CompressionQuality.Balanced;
        using var encoded=new MemoryStream();
        encoder.EncodeToDds(bgra,width,height,PixelFormat.Bgra32).Write(encoded);
        encoded.Position=0;return new DdsFile(encoded);
    }

    static byte Encode(float value)=>(byte)Math.Clamp((int)MathF.Round((value+1)*127.5f),0,255);
}
