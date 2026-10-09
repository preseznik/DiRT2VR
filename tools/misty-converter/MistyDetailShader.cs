using System.Xml.Linq;
using System.Buffers.Binary;
using System.Text;
using DiRT2VR.Nordschleife;

internal static class MistyDetailShader
{
    internal const string Ground="terrain_misty_ground",Grass="misty_grass";
    internal const string GroundProgram=Ground+".fp",GrassProgram=Grass+".fp";
    internal static void Add(XDocument document,string sourceFile,string output)
    {
        var original=new XDocument(document);
        var group=document.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="terrain_infield");
        var program=document.Descendants("SHADERPROGRAM").Single(g=>(string?)g.Attribute("id")=="terrain_infield.cg.fp");
        string hlsl=File.ReadAllText(sourceFile);
        foreach(string name in new[]{Ground,Grass})
        {
            byte[] bytes=MistyShaderCompiler.Compile((name==Grass?"#define MISTY_GRASS\n":"")+hlsl,name);
            var nextProgram=new XElement(program);nextProgram.SetAttributeValue("id",name+".fp");
            var code=nextProgram.Element("SHADERPROGRAMCODE")!;
            code.SetAttributeValue("codeSize",bytes.Length);
            // PSSG's byte XML parser consumes space-separated byte tokens.
            code.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",bytes.Select(b=>b.ToString("X2")));
            foreach(string texture in new[]{"TMistyDetail","TMistyDetailA","TMistyNormal"})code.Add(new XElement("SHADERINPUTDEFINITION",new XAttribute("name",texture),new XAttribute("type","texture")));
            var resources=TextureBindings(bytes);
            // EGO dereferences every declared program binding, including unused
            // textures dropped by the compiler. Declare only reflected resources.
            code.Elements("SHADERINPUTDEFINITION").Where(d=>(string?)d.Attribute("type")=="texture"&&!resources.Contains((string)d.Attribute("name")!)).Remove();
            if(!resources.SetEquals(code.Elements("SHADERINPUTDEFINITION").Where(d=>(string?)d.Attribute("type")=="texture").Select(d=>(string)d.Attribute("name")!)))
                throw new InvalidDataException("Unbound compiled texture resource.");
            code.SetAttributeValue("parameterCount",code.Elements("SHADERINPUTDEFINITION").Count());
            var nextGroup=new XElement(group);nextGroup.SetAttributeValue("id",name);
            foreach(string texture in new[]{"TMistyDetail","TMistyDetailA","TMistyNormal"})nextGroup.Elements("SHADERINPUTDEFINITION").Last().AddAfterSelf(new XElement("SHADERINPUTDEFINITION",new XAttribute("name",texture),new XAttribute("type","texture")));
            nextGroup.SetAttributeValue("parameterCount",nextGroup.Elements("SHADERINPUTDEFINITION").Count());
            foreach(var pass in nextGroup.Elements("SHADERGROUPPASS").ToArray())
            {
                string mask=pass.Attribute("passConfigMask")!.Value.Trim();
                if(name==Grass&&mask=="00 00 00 04"){pass.Remove();continue;}
                if(mask is "00 00 00 01" or "00 00 00 40")pass.SetAttributeValue("fragmentProgram","#"+name+".fp");
                if(name==Grass)pass.SetAttributeValue("cullFaceType","NONE");
            }
            nextGroup.SetAttributeValue("passCount",nextGroup.Elements("SHADERGROUPPASS").Count());
            group.Parent!.Add(nextGroup);program.Parent!.Add(nextProgram);
            File.WriteAllBytes(Path.Combine(output,name+".dxbc"),bytes);
        }
        var check=new XDocument(document);
        foreach(var item in check.Descendants().Where(e=>(string?)e.Attribute("id") is Ground or Grass or GroundProgram or GrassProgram).ToArray())item.Remove();
        if(!XNode.DeepEquals(check,original))throw new InvalidDataException("Detail shader modified existing shader programs.");
        Files.Json(Path.Combine(output,"detail-shader-audit.json"),new{Passed=true,AddedGroups=new[]{Ground,Grass},OtherStockShadersAndStatesUnchanged=true,NativeVertexProgramsRetained=true,DryRoadSpecularZero=true,GrassFadeMetres=new[]{70,105},LabOnly=true});
    }
    static HashSet<string> TextureBindings(byte[] bytes)
    {
        int U(int at)=>checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at,4)));
        int start=Enumerable.Range(0,U(28)).Select(i=>U(32+i*4)).Single(i=>Encoding.ASCII.GetString(bytes,i,4)=="RDEF")+8;
        string Name(int at){int end=Array.IndexOf(bytes,(byte)0,at);return Encoding.ASCII.GetString(bytes,at,end-at);}
        var textures=new HashSet<string>(StringComparer.Ordinal);var buffers=new HashSet<int>();
        for(int i=0;i<U(start+8);i++)
        {
            int at=start+U(start+12)+i*32,type=U(at+4);
            if(type==2)textures.Add(Name(start+U(at)));
            if(type==0)buffers.Add(U(at+20));
        }
        if(!buffers.SetEquals(new[]{0,1,2,3}))throw new InvalidDataException("Native terrain ABI needs contiguous global, frame, target and camera buffers.");
        return textures;
    }
}
