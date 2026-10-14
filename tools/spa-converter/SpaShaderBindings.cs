using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;

internal static class SpaShaderBindings
{
    internal static void Validate(XElement code)
    {
        byte[] bytes=Convert.FromHexString(string.Concat(code.Element("SHADERPROGRAMCODEBLOCK")!.Value.Where(c=>!char.IsWhiteSpace(c))));
        int U(int at)=>checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at,4)));
        int start=Enumerable.Range(0,U(28)).Select(i=>U(32+i*4)).Single(i=>Encoding.ASCII.GetString(bytes,i,4)=="RDEF")+8;
        string Name(int offset){int at=start+offset,end=Array.IndexOf(bytes,(byte)0,at);return Encoding.ASCII.GetString(bytes,at,end-at);}
        var textures=new HashSet<string>();var constants=new HashSet<string>();var slots=new HashSet<int>();
        for(int i=0;i<U(start+8);i++)
        {
            int at=start+U(start+12)+32*i,type=U(at+4);
            if(type==2)textures.Add(Name(U(at)));
            if(type==0)slots.Add(U(at+20));
        }
        for(int i=0;i<U(start);i++)
        {
            int at=start+U(start+4)+24*i;if(Name(U(at))!="$Globals")continue;
            for(int j=0;j<U(at+4);j++)constants.Add(Name(U(start+U(at+8)+40*j)));
        }
        if(!slots.SetEquals(new[]{0,1,2,3}))throw new InvalidDataException("Terrain constant buffer ABI mismatch");
        var declarations=code.Elements("SHADERINPUTDEFINITION").ToArray();
        var bad=declarations.Where(d=>!((string?)d.Attribute("type")=="texture"?textures:constants).Contains((string)d.Attribute("name")!)).Select(d=>(string)d.Attribute("name")!).ToArray();
        if(bad.Length>0)throw new InvalidDataException("Pixel inputs have no compiled binding: "+string.Join(", ",bad));
        if(!textures.SetEquals(declarations.Where(d=>(string?)d.Attribute("type")=="texture").Select(d=>(string)d.Attribute("name")!)))throw new InvalidDataException("Compiled texture has no program declaration");
        if(!constants.SetEquals(declarations.Where(d=>(string?)d.Attribute("type")=="constant").Select(d=>(string)d.Attribute("name")!)))throw new InvalidDataException("Compiled global has no program declaration");
    }
}
