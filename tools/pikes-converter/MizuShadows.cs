using EgoEngineLibrary.Graphics.Dds;

internal static class MizuShadows
{
    // GRID 2's GIS map stores sky occlusion in BC3 alpha. DiRT 2's decal
    // shader uses R for sun shadow, G for specular occlusion and B for diffuse
    // lighting. Keep source sun occlusion separate from baked indirect light:
    // copying alpha into B erases paint; leaving B white makes tunnel paint glow.
    internal static void FromAlpha(DdsFile file)
    {
        if(file.header.ddspf.fourCC!=0x35545844)throw new InvalidDataException("Expected BC3 source shadow map");
        using var output=new MemoryStream();int offset=0;
        for(uint level=0;level<Math.Max(1,file.header.mipMapCount);level++)
        {
            int width=Math.Max(1,(int)(file.header.width>>(int)level)),height=Math.Max(1,(int)(file.header.height>>(int)level));
            var pixels=new byte[checked(width*height*4)];
            for(int by=0;by<(height+3)/4;by++)for(int bx=0;bx<(width+3)/4;bx++)
            {
                if(offset+16>file.bdata.Length)throw new InvalidDataException("Truncated BC3 shadow map");
                var block=file.bdata.AsSpan(offset,16);offset+=16;var alpha=new byte[8];alpha[0]=block[0];alpha[1]=block[1];
                if(alpha[0]>alpha[1])for(int i=2;i<8;i++)alpha[i]=(byte)(((8-i)*alpha[0]+(i-1)*alpha[1])/7);
                else {for(int i=2;i<6;i++)alpha[i]=(byte)(((6-i)*alpha[0]+(i-1)*alpha[1])/5);alpha[6]=0;alpha[7]=255;}
                ulong bits=0;for(int i=0;i<6;i++)bits|=(ulong)block[2+i]<<(8*i);
                var colours=new (int R,int G,int B)[4];
                static (int R,int G,int B) RGB(int value)
                {
                    int r=(value>>11)&31,g=(value>>5)&63,b=value&31;
                    return ((r<<3)|(r>>2),(g<<2)|(g>>4),(b<<3)|(b>>2));
                }
                colours[0]=RGB(block[8]|block[9]<<8);colours[1]=RGB(block[10]|block[11]<<8);
                for(int i=2;i<4;i++)colours[i]=(((4-i)*colours[0].R+(i-1)*colours[1].R)/3,
                    ((4-i)*colours[0].G+(i-1)*colours[1].G)/3,((4-i)*colours[0].B+(i-1)*colours[1].B)/3);
                uint colourBits=(uint)(block[12]|block[13]<<8|block[14]<<16|block[15]<<24);
                for(int y=0;y<4;y++)for(int x=0;x<4;x++)
                {
                    if(bx*4+x>=width||by*4+y>=height)continue;
                    byte value=alpha[(int)((bits>>(3*(y*4+x)))&7)];int pixel=((by*4+y)*width+bx*4+x)*4;
                    var colour=colours[(colourBits>>(2*(y*4+x)))&3];
                    pixels[pixel]=(byte)((77*colour.R+150*colour.G+29*colour.B+128)>>8);
                    pixels[pixel+1]=pixels[pixel+2]=value;pixels[pixel+3]=255;
                }
            }
            output.Write(pixels);
        }
        if(offset!=file.bdata.Length)throw new InvalidDataException("Unexpected BC3 shadow payload");
        file.bdata=output.ToArray();file.header.ddspf.fourCC=0;
        file.header.ddspf.flags=DdsPixelFormat.Flags.DDPF_RGB|DdsPixelFormat.Flags.DDPF_ALPHAPIXELS;
        file.header.ddspf.rGBBitCount=32;file.header.ddspf.rBitMask=0x00ff0000;file.header.ddspf.gBitMask=0x0000ff00;
        file.header.ddspf.bBitMask=0x000000ff;file.header.ddspf.aBitMask=0xff000000;file.header.pitchOrLinearSize=file.header.width*4;
        file.header.flags=(file.header.flags&~DdsHeader.Flags.DDSD_LINEARSIZE)|DdsHeader.Flags.DDSD_PITCH;
    }
}
