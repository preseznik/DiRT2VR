using EgoEngineLibrary.Graphics.Dds;
using System.Numerics;

namespace DiRT2VR.CustomCars;

internal static class RawTexture
{
    internal static bool Prepare(DdsFile dds)
    {
        if(dds.header.ddspf.fourCC!=0)return false;
        var pf=dds.header.ddspf;
        if(pf.rGBBitCount==32 && pf.rBitMask==0xff0000 && pf.gBitMask==0xff00 && pf.bBitMask==0xff && pf.aBitMask==0xff000000)return false;
        bool luminance=pf.flags.HasFlag(DdsPixelFormat.Flags.DDPF_LUMINANCE);
        if(pf.rGBBitCount is not (8 or 16 or 24 or 32) || pf.rBitMask==0 || (!luminance && (pf.gBitMask==0 || pf.bBitMask==0)) || dds.bdata2.Count!=0)throw new InvalidDataException($"Unsupported raw DDS layout bits={pf.rGBBitCount} masks={pf.rBitMask:x}/{pf.gBitMask:x}/{pf.bBitMask:x}/{pf.aBitMask:x} flags={pf.flags} cube={dds.bdata2.Count}");
        int levels=(int)Math.Max(1,dds.header.mipMapCount);if(levels>32)throw new InvalidDataException("Invalid DDS mip count");
        int pixels=0;uint width=dds.header.width,height=dds.header.height;
        for(int i=0;i<levels;i++){pixels=checked(pixels+(int)checked(width*height));width=Math.Max(1,width/2);height=Math.Max(1,height/2);}
        int stride=(int)pf.rGBBitCount/8;
        if(dds.bdata.Length!=checked(pixels*stride))throw new InvalidDataException("Raw DDS payload/mip dimensions differ");
        static byte Channel(uint value,uint mask,byte fallback)
        {
            if(mask==0)return fallback;
            int shift=BitOperations.TrailingZeroCount(mask);uint max=mask>>shift;
            if((max&(max+1))!=0 || max>255)throw new InvalidDataException("Unsupported DDS channel mask");
            return (byte)((((value&mask)>>shift)*255+max/2)/max);
        }
        var output=new byte[checked(pixels*4)];
        for(int i=0;i<pixels;i++)
        {
            uint value=0;for(int k=0;k<stride;k++)value|=(uint)dds.bdata[i*stride+k]<<(8*k);
            output[4*i]=Channel(value,luminance?pf.rBitMask:pf.bBitMask,0);output[4*i+1]=Channel(value,luminance?pf.rBitMask:pf.gBitMask,0);output[4*i+2]=Channel(value,pf.rBitMask,0);output[4*i+3]=Channel(value,pf.aBitMask,255);
        }
        dds.bdata=output;dds.header.pitchOrLinearSize=dds.header.width*4;
        dds.header.ddspf.flags=DdsPixelFormat.Flags.DDPF_RGB|DdsPixelFormat.Flags.DDPF_ALPHAPIXELS;
        dds.header.ddspf.rGBBitCount=32;dds.header.ddspf.rBitMask=0xff0000;dds.header.ddspf.gBitMask=0xff00;dds.header.ddspf.bBitMask=0xff;dds.header.ddspf.aBitMask=0xff000000;
        return true;
    }
}
