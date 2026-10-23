using System.Buffers.Binary;
using System.Text.Json;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class GroundColourRange
{
    internal static (byte[] Scale,byte[] Min) Read(PssgFile source,PssgTexture texture)
    {
        var user=texture.ChildElements.SingleOrDefault(e=>e.Name=="USERDATA");
        if(user is null){var one=new byte[16];for(int i=0;i<4;i++)BinaryPrimitives.WriteSingleBigEndian(one.AsSpan(i*4),1);return(one,new byte[16]);}
        string id=user.GetAttributeValue<string>("object").TrimStart('#');
        var data=source.Elements<PssgElement>().Single(e=>e.Name=="PNTEXTURESCALING"&&e.GetAttributeValue<string>("id")==id);
        byte[] scale=(byte[])data.Attributes["scale"].Value,min=(byte[])data.Attributes["min"].Value;
        if(scale.Length!=16||min.Length!=16)throw new InvalidDataException("Invalid source texture colour range");
        foreach(var bytes in new[]{scale,min})for(int i=0;i<4;i++)if(!float.IsFinite(BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(i*4))))throw new InvalidDataException("Non-finite source colour range");
        return((byte[])scale.Clone(),(byte[])min.Clone());
    }
}
