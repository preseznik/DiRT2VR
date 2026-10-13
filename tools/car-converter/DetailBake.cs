using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using EgoEngineLibrary.Graphics.Dds;

// Profile-specific colour approximation: diffuse RGB times the detail/white
// blend selected by diffuse alpha. Reflection, flakes and damage remain native
// donor approximations. See Content Manager DarkMaterial.Unpack.fx, Unpack_Maps.
namespace DiRT2VR.CustomCars;

internal static class DetailBake
{
    internal static DdsFile Compose(byte[] diffuse, byte[] detail, float multiplier)
    {
        if(!float.IsFinite(multiplier) || multiplier<0 || multiplier>4096)throw new InvalidDataException("Invalid detail UV multiplier");
        var (basePixels,width,height)=Decode(diffuse);
        var (detailPixels,dw,dh)=Decode(detail);
        var bgra=new byte[checked(width*height*4)];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var b=basePixels[y*width+x];
            var d=Sample(detailPixels,dw,dh,(x+.5f)/width*multiplier,(y+.5f)/height*multiplier);
            var color=new Vector3(b.r,b.g,b.b)/255*Vector3.Lerp(d,Vector3.One,b.a/255f);
            int i=(y*width+x)*4;bgra[i]=Byte(color.Z);bgra[i+1]=Byte(color.Y);bgra[i+2]=Byte(color.X);bgra[i+3]=255;
        }
        // Keep the native 32-bit game's texture allocation comparable to the
        // original compressed AC atlases. Baking does not require raw RGBA in game.
        var encoder=new BcEncoder(CompressionFormat.Bc3);
        encoder.OutputOptions.GenerateMipMaps=true;encoder.OutputOptions.Quality=CompressionQuality.Balanced;
        var compressed=encoder.EncodeToDds(bgra,width,height,PixelFormat.Bgra32);
        using var encoded=new MemoryStream();compressed.Write(encoded);encoded.Position=0;return new DdsFile(encoded);
    }
    static byte Byte(float value)=>(byte)Math.Clamp((int)MathF.Round(value*255),0,255);
    static Vector3 Sample(ColorRgba32[] data,int w,int h,float u,float v)
    {
        float x=(u-MathF.Floor(u))*w-.5f,y=(v-MathF.Floor(v))*h-.5f;
        int ix=(int)MathF.Floor(x),iy=(int)MathF.Floor(y);float fx=x-ix,fy=y-iy;
        Vector3 At(int px,int py){var c=data[((py%h+h)%h)*w+(px%w+w)%w];return new(c.r/255f,c.g/255f,c.b/255f);}
        return Vector3.Lerp(Vector3.Lerp(At(ix,iy),At(ix+1,iy),fx),Vector3.Lerp(At(ix,iy+1),At(ix+1,iy+1),fx),fy);
    }
    internal static (ColorRgba32[] Pixels,int Width,int Height) Decode(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false);var dds=new DdsFile(stream);
        int w=checked((int)dds.header.width),h=checked((int)dds.header.height);
        if(w<1 || h<1 || w>8192 || h>8192 || dds.bdata2.Count!=0)throw new InvalidDataException("Invalid bake texture dimensions");
        if(dds.header.ddspf.fourCC!=0)
        {
            var format=dds.header.ddspf.fourCC switch{0x31545844=>CompressionFormat.Bc1,0x33545844=>CompressionFormat.Bc2,0x35545844=>CompressionFormat.Bc3,_=>throw new InvalidDataException("Unsupported bake DDS compression")};
            int length=checked(((w+3)/4)*((h+3)/4)*(format==CompressionFormat.Bc1?8:16));
            if(dds.bdata.Length<length)throw new InvalidDataException("Truncated compressed bake DDS");
            return(new BcDecoder().DecodeRaw(dds.bdata[..length],w,h,format),w,h);
        }
        RawTexture.Prepare(dds);var pixels=new ColorRgba32[w*h];
        for(int i=0;i<pixels.Length;i++)pixels[i]=new(dds.bdata[i*4+2],dds.bdata[i*4+1],dds.bdata[i*4],dds.bdata[i*4+3]);
        return(pixels,w,h);
    }
}
