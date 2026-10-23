using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

internal static class BakedLightingProbe
{
    internal sealed class LightMap
    {
        readonly byte[] bytes;readonly bool coloured;readonly int coverage,stride;public int Resolution{get;}public float OriginX{get;}public float OriginZ{get;}public float ScaleX{get;}public float ScaleZ{get;}
        internal LightMap(string path,bool coloured)
        {
            bytes=File.ReadAllBytes(path);this.coloured=coloured;uint expected=coloured?0x14020002u:0x14010002u;if(BinaryPrimitives.ReadUInt32LittleEndian(bytes)!=expected)throw new InvalidDataException("Unexpected source lightmap type");
            OriginX=BitConverter.ToSingle(bytes,4);OriginZ=BitConverter.ToSingle(bytes,8);ScaleX=BitConverter.ToSingle(bytes,12);ScaleZ=BitConverter.ToSingle(bytes,16);Resolution=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20));stride=coloured?260:68;
            if(Resolution is <1 or >4096||BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24))!=8||(bytes.Length-28)%stride!=0)throw new InvalidDataException("Invalid source lightmap dimensions");coverage=8;while(coverage<Resolution)coverage*=8;
            for(int node=28;node<bytes.Length;node+=stride)for(int cell=0;cell<64;cell++)if(coloured){int at=node+4+cell*4;int flag=bytes[at+3];if(flag>64||flag!=0&&(bytes[at]!=0||bytes[at+1]!=0||bytes[at+2]!=0))throw new InvalidDataException("Invalid colour lightmap branch");}
        }
        internal DdsFile Decode()
        {
            var pixels=new byte[checked(Resolution*Resolution*4)];
            for(int z=0;z<Resolution;z++)for(int x=0;x<Resolution;x++)
            {
                int px=x,pz=z,node=28,size=coverage,red=0,green=0,blue=0;bool leaf=false;
                while(size>1)
                {
                    size/=8;int cell=pz/size*8+px/size;px%=size;pz%=size;int at=node+4+cell*(coloured?4:1);
                    int branch=coloured?bytes[at+3]:unchecked((sbyte)bytes[at])<0?-unchecked((sbyte)bytes[at]):0;
                    // Colour cells are little-endian packed BGR with the high
                    // byte reserved for a child index, like uint_color_argb.
                    if(branch==0){if(coloured){red=bytes[at+2];green=bytes[at+1];blue=bytes[at];}else red=green=blue=(int)MathF.Round(bytes[at]/127f*255);leaf=true;break;}
                    node=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(node))+(branch-1)*stride);if(node<28||node+stride>bytes.Length||(node-28)%stride!=0)throw new InvalidDataException("Invalid source lightmap pointer");
                }
                if(!leaf)throw new InvalidDataException("Missing source lightmap leaf");int pixel=(z*Resolution+x)*4;pixels[pixel]=(byte)blue;pixels[pixel+1]=(byte)green;pixels[pixel+2]=(byte)red;pixels[pixel+3]=255;
            }
            var file=new DdsFile();file.header.width=file.header.height=(uint)Resolution;file.header.mipMapCount=1;file.header.pitchOrLinearSize=(uint)Resolution*4;
            file.header.ddspf.flags=DdsPixelFormat.Flags.DDPF_RGB|DdsPixelFormat.Flags.DDPF_ALPHAPIXELS;file.header.ddspf.rGBBitCount=32;file.header.ddspf.rBitMask=0x00ff0000;file.header.ddspf.gBitMask=0x0000ff00;file.header.ddspf.bBitMask=0x000000ff;file.header.ddspf.aBitMask=0xff000000;file.bdata=pixels;return file;
        }
    }
}
