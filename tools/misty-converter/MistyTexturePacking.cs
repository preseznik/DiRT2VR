using System.Security.Cryptography;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Dds;

internal sealed class MistyTexturePacking(Scene scene)
{
    readonly Dictionary<string,string> packed=new(StringComparer.Ordinal);
    internal string Convert(string name,string operation)
    {
        string key=name+"/"+operation;
        if(packed.TryGetValue(key,out var found))return found;
        var source=scene.Textures[name];
        using var input=new MemoryStream(source.Dds,false);
        var dds=new DdsFile(input);
        if(operation is "matte" or "road-shading")
        {
            if(operation=="road-shading")RoadMaterial.PackShading(dds);
            else
            {
                // Alter compressed alpha only. Re-encoding RGB would soften the grain.
                if(dds.header.ddspf.fourCC==0x31545844)
                {
                    if(dds.bdata.Length%8!=0)throw new InvalidDataException("Incomplete dry-road BC1 texture");
                    var blocks=new byte[checked(dds.bdata.Length*2)];
                    for(int i=0;i<dds.bdata.Length;i+=8)
                    {
                        var block=dds.bdata.AsSpan(i,8);
                        if(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(block)<=System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(block[2..]))
                            throw new InvalidDataException("Three-colour BC1 cannot be copied losslessly to a dry-road BC3 texture");
                        block.CopyTo(blocks.AsSpan(i*2+8,8));
                    }
                    dds.bdata=blocks;dds.header.ddspf.fourCC=0x35545844;
                    dds.header.pitchOrLinearSize=((dds.header.width+3)/4)*((dds.header.height+3)/4)*16;
                }
                if(dds.header.ddspf.fourCC is not (0x33545844 or 0x35545844)||dds.bdata.Length%16!=0)
                    throw new InvalidDataException("Unsupported dry-road DDS format: "+name);
                for(int i=0;i<dds.bdata.Length;i+=16)dds.bdata.AsSpan(i,8).Clear();
            }
            using var resultBytes=new MemoryStream();dds.Write(resultBytes);var payloadBytes=resultBytes.ToArray();
            string alias="misty_"+operation+"_"+source.Hash[..20]+".dds";
            scene.Textures[alias]=new(payloadBytes,ConvertHash(payloadBytes));packed[key]=alias;return alias;
        }
        if(operation=="opaque")
        {
            DdsAlpha.Prepare(dds,true);using var opaque=new MemoryStream();dds.Write(opaque);
            var data=opaque.ToArray();string id="misty_opaque_"+source.Hash[..20]+".dds";
            scene.Textures[id]=new(data,ConvertHash(data));packed[key]=id;return id;
        }
        using var decoderInput=new MemoryStream(source.Dds,false);
        var levels=new BcDecoder().DecodeAllMipMaps(BCnEncoder.Shared.ImageFiles.DdsFile.Load(decoderInput));
        using var bytes=new MemoryStream();
        var encoder=new BcEncoder();encoder.OutputOptions.Format=CompressionFormat.Bc3;
        encoder.OutputOptions.GenerateMipMaps=false;encoder.OutputOptions.Quality=CompressionQuality.Balanced;
        int width=(int)dds.header.width,height=(int)dds.header.height;
        foreach(var level in levels)
        {
            var pixels=new byte[level.Length*4];
            for(int i=0;i<level.Length;i++)
            {
                int sample=operation=="mirror-v"?(height-1-i/width)*width+i%width:i;
                var p=level[sample];
                var q=operation switch{
                    // AC RGB tangent normal -> EGO DXT5nm: X in alpha, Y in green.
                    "normal"=>new ColorRgba32(255,p.g,255,p.r),
                    "opaque"=>new ColorRgba32(p.r,p.g,p.b,255),
                    "shading"=>new ColorRgba32(255,255,p.b,255),
                    "mirror-v"=>p,
                    _=>throw new InvalidDataException("Unknown texture conversion")};
                int at=i*4;pixels[at]=q.r;pixels[at+1]=q.g;pixels[at+2]=q.b;pixels[at+3]=q.a;
            }
            bytes.Write(encoder.EncodeToRawBytes(pixels,width,height,PixelFormat.Rgba32)[0]);
            width=Math.Max(1,width/2);height=Math.Max(1,height/2);
        }
        dds.header.ddspf=new DdsPixelFormat{size=32,flags=DdsPixelFormat.Flags.DDPF_FOURCC,fourCC=0x35545844};
        dds.header.pitchOrLinearSize=((dds.header.width+3)/4)*((dds.header.height+3)/4)*16;
        dds.bdata=bytes.ToArray();
        using var output=new MemoryStream();dds.Write(output);var payload=output.ToArray();
        string result="misty_"+operation+"_"+source.Hash[..20]+".dds";
        scene.Textures[result]=new(payload,ConvertHash(payload));packed[key]=result;return result;
    }
    internal static string ConvertHash(byte[] bytes)=>System.Convert.ToHexString(SHA256.HashData(bytes));
}
