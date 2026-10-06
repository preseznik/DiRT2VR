using System.Buffers.Binary;
using EgoEngineLibrary.Graphics.Dds;

namespace DiRT2VR.Nordschleife;

internal static class DdsAlpha
{
    internal static void Prepare(DdsFile file,bool opaque)
    {
        if(opaque)Opaque(file);else ExpandLuminance(file,false);
    }
    static bool ExpandLuminance(DdsFile file,bool opaque)
    {
        if(file.header.ddspf.fourCC!=0 || (file.header.ddspf.flags&DdsPixelFormat.Flags.DDPF_LUMINANCE)==0 || file.header.ddspf.rGBBitCount is not (8 or 16))return false;
        int stride=checked((int)file.header.ddspf.rGBBitCount/8);
        if(file.header.ddspf.rBitMask!=255 || file.header.ddspf.gBitMask!=0 || file.header.ddspf.bBitMask!=0 || file.header.ddspf.aBitMask is not (0 or 65280) || file.bdata.Length%stride!=0)throw new InvalidDataException("Unsupported luminance DDS layout.");
        var pixels=new byte[checked(file.bdata.Length/stride*4)];
        for(int i=0;i<file.bdata.Length/stride;i++)
        {
            byte l=file.bdata[i*stride];pixels[i*4]=l;pixels[i*4+1]=l;pixels[i*4+2]=l;
            pixels[i*4+3]=!opaque&&stride==2&&file.header.ddspf.aBitMask!=0?file.bdata[i*stride+1]:(byte)255;
        }
        file.bdata=pixels;file.header.ddspf.flags=DdsPixelFormat.Flags.DDPF_RGB|DdsPixelFormat.Flags.DDPF_ALPHAPIXELS;
        file.header.ddspf.rGBBitCount=32;file.header.ddspf.rBitMask=0x00ff0000;file.header.ddspf.gBitMask=0x0000ff00;file.header.ddspf.bBitMask=0x000000ff;file.header.ddspf.aBitMask=0xff000000;
        file.header.pitchOrLinearSize=checked(file.header.width*4);
        return true;
    }
    // AC opaque materials can store blend/specular masks in diffuse alpha. DiRT 2's
    // object shader uses that channel as coverage; preserve compressed RGB, make coverage solid.
    internal static void Opaque(DdsFile file)
    {
        uint format=file.header.ddspf.fourCC;
        // EGO needs explicit RGB channels for AC's luminance formats.
        if(ExpandLuminance(file,true))return;
        if(format is 0x33545844 or 0x35545844) // DXT3 / DXT5
        {
            if(file.bdata.Length%16!=0)throw new InvalidDataException("Incomplete opaque DDS block.");
            for(int i=0;i<file.bdata.Length;i+=16)
            {
                var alpha=file.bdata.AsSpan(i,8);
                if(format==0x33545844)alpha.Fill(255);
                else {alpha.Clear();alpha[0]=255;alpha[1]=255;}
            }
        }
        else if(format==0&&file.header.ddspf.rGBBitCount==32)
        {
            uint mask=file.header.ddspf.aBitMask;
            if(file.bdata.Length%4!=0)throw new InvalidDataException("Incomplete opaque DDS pixel.");
            for(int i=0;i<file.bdata.Length;i+=4)
            {
                var pixel=file.bdata.AsSpan(i,4);
                BinaryPrimitives.WriteUInt32LittleEndian(pixel,BinaryPrimitives.ReadUInt32LittleEndian(pixel)|mask);
            }
        }
        else if(format!=0x31545844)throw new InvalidDataException("Unsupported opaque DDS format.");
    }
}
