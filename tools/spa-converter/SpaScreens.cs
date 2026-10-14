using System.Globalization;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal sealed class SpaScreens
{
    internal const string Alias="object_spa_screen";
    readonly XElement group;
    readonly ShaderInputInfo layout;
    internal static XElement[] Additions()=>[
        new("SHADERINPUTDEFINITION",new XAttribute("name","ScreenBrightness"),new XAttribute("type","constant"),new XAttribute("format","float")),
        new("SHADERINPUTDEFINITION",new XAttribute("name","ScreenFrames"),new XAttribute("type","constant"),new XAttribute("format","float2"))];
    internal SpaScreens(string dirt,Dictionary<string,string> inputs)
    {
        string path=Files.Inside(dirt,"tracks/london/battersea/objects.pssg");inputs.TryAdd(path,Files.Hash(path));var file=Files.Pssg(path);
        var root=new XElement("root");file.GetObject<PssgShaderGroup>("object_simple.fx".AsMemory()).WriteXml(root);
        group=root.Elements().Single();group.SetAttributeValue("id",Alias+".fx");group.Add(Additions());
        group.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        layout=new(Alias+".fx",ShaderInputInfo.CreateFromPssg(file).Single(l=>l.ShaderGroupId=="object_simple.fx").BlockInputs);
    }
    internal NativeMaterial Convert(Dictionary<string,XElement> source,Dictionary<string,string> textures)
    {
        var definitions=group.Elements("SHADERINPUTDEFINITION").ToArray();
        var instance=new XElement("SHADERINSTANCE",new XAttribute("id","spa_screen"),new XAttribute("shaderGroup","#"+Alias+".fx"),new XAttribute("parameterCount",definitions.Length),new XAttribute("renderSortPriority",0));
        void Set(string name,string value)
        {
            int index=Array.FindIndex(definitions,d=>(string?)d.Attribute("name")==name);var d=definitions[index];
            var i=new XElement("SHADERINPUT",new XAttribute("parameterID",index),new XAttribute("type",d.Attribute("type")!.Value));
            if(d.Attribute("format") is {} format)i.SetAttributeValue("format",format.Value);
            if((string?)d.Attribute("type")=="texture")i.SetAttributeValue("texture",value);else i.Value=value;
            instance.Add(i);
        }
        float Number(string name)=>float.Parse(source[name].Value,CultureInfo.InvariantCulture);
        if(Number("framesU")!=1||Number("framesV")!=4||Number("framesN")!=4)throw new InvalidDataException("Unexpected Spa screen atlas");
        Set("TDiffuseAlphaMap","screen");textures["TDiffuseAlphaMap"]=textures["txDiffuse"];
        Set("ScreenBrightness",source["brightness"].Value);Set("ScreenFrames","1 4");
        instance.SetAttributeValue("parameterSavedCount",instance.Elements().Count());
        // Use the object's own depth pass, as other object aliases do. The
        // terrain batch-depth contract only contains terrain/object_simple.
        return new(group,instance,layout,false);
    }
    internal static void Shader(XDocument original,XDocument next,string output)
    {
        var group=new XElement(original.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="object_simple"));group.SetAttributeValue("id",Alias);
        var tail=group.Elements().Where(e=>e.Name.LocalName is "SHADERSTREAMDEFINITION" or "SHADERGROUPPASS").ToArray();tail.Remove();group.Add(Additions());group.Add(tail);
        group.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        foreach(var pass in group.Elements("SHADERGROUPPASS").Where(p=>p.Attribute("passConfigMask")!.Value.Trim() is "00 00 00 01" or "00 00 00 40" or "00 00 00 80"))
        {
            pass.SetAttributeValue("vertexProgram","#object_simple.object_simple.cg.vp");pass.SetAttributeValue("fragmentProgram","#"+Alias+".fp");
            pass.SetAttributeValue("blendEnable",0);pass.SetAttributeValue("alphaTestEnable",0);
        }
        var program=new XElement(original.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")=="object_simple.cg.fp"));program.SetAttributeValue("id",Alias+".fp");
        var code=program.Element("SHADERPROGRAMCODE")!;code.Elements("SHADERINPUTDEFINITION").Remove();
        code.Add(new XElement("SHADERINPUTDEFINITION",new XAttribute("name","TDiffuseAlphaMap"),new XAttribute("type","texture")));code.Add(Additions());code.SetAttributeValue("parameterCount",3);
        byte[] bytes=SpaShaderCompiler.Compile(SpaResources.Text("SpaScreen.hlsl"),"ps_5_0");
        code.SetAttributeValue("codeSize",bytes.Length);code.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",bytes.Select(b=>b.ToString("X2")));
        next.Descendants("LIBRARY").Single(l=>(string?)l.Attribute("type")=="SHADERGROUP").Add(group);
        next.Descendants("LIBRARY").Single(l=>(string?)l.Attribute("type")=="SHADERPROGRAM").Add(program);
        File.WriteAllBytes(Path.Combine(output,Alias+".dxbc"),bytes);
    }
}
