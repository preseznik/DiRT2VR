using System.IO.Compression;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class PikesSponsorBarrier
{
    internal static PssgFile Read(string path)
    {
        using var raw=File.OpenRead(path);using var memory=new MemoryStream();
        if(raw.ReadByte()==0x1f){raw.Position=0;using var zip=new GZipStream(raw,CompressionMode.Decompress,true);zip.CopyTo(memory);}else{raw.Position=0;raw.CopyTo(memory);}memory.Position=0;return PssgFile.Open(memory);
    }
    static float F(string value)=>float.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
    static Matrix4x4 Matrix(string value){var a=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();if(a.Length!=16)throw new InvalidDataException("Invalid source matrix");return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
    static Matrix4x4 World(PssgNode node){var result=node.Transform.Transform;for(var p=node.ParentElement;p is not null;p=p.ParentElement)if(p is PssgNode n)result*=n.Transform.Transform;return result;}
    static IEnumerable<PssgNode> Nodes(PssgNode root)=>new[]{root}.Concat(root.ChildElements.OfType<PssgNode>().SelectMany(Nodes));
}
