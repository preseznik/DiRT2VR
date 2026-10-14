using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg;

internal static class SpaShaders
{
    internal static readonly string[] Sources=["terrain_infield.fx","terrain_track_infield.fx","terrain_edge.fx","terrain_road_blend_flat.fx","terrain_simple.fx"];
    static string Define(string source)=>source switch {
        "terrain_track_infield.fx"=>"#define SPA_TRACK\n", "terrain_edge.fx"=>"#define SPA_EDGE\n",
        "terrain_road_blend_flat.fx"=>"#define SPA_ROAD\n", "terrain_simple.fx"=>"#define SPA_SIMPLE\n", _=>"" };
    internal static string Alias(string source)=>source.Replace("terrain_","terrain_spa_",StringComparison.Ordinal);
    internal static XElement[] Additions()=>[
        new("SHADERINPUTDEFINITION",new XAttribute("name","TColourMap"),new XAttribute("type","texture")),
        new("SHADERINPUTDEFINITION",new XAttribute("name","ColourMapScale"),new XAttribute("type","constant"),new XAttribute("format","float")),
        new("SHADERINPUTDEFINITION",new XAttribute("name","TColourMapChannelScale"),new XAttribute("type","constant"),new XAttribute("format","float4")),
        new("SHADERINPUTDEFINITION",new XAttribute("name","TColourMapChannelMin"),new XAttribute("type","constant"),new XAttribute("format","float4")),
        new("SHADERINPUTDEFINITION",new XAttribute("name","SourceLighting"),new XAttribute("type","constant"),new XAttribute("format","float4"))];
    internal static void Write(string dirt,string output)
    {
        string input=Files.Inside(dirt,"dx11/shaderpack/shaderpack.pssg");
        var original=Files.Document(Files.Pssg(input));var next=new XDocument(original);
        var reports=new List<object>();
        foreach(string source in Sources)
        {
            string stock=source=="terrain_track_infield.fx"?"terrain_infield":source[..^3],alias=Alias(source)[..^3];
            var group=new XElement(original.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")==stock));group.SetAttributeValue("id",alias);
            var tail=group.Elements().Where(e=>e.Name.LocalName is "SHADERSTREAMDEFINITION" or "SHADERGROUPPASS").ToArray();tail.Remove();
            group.Add(Additions());group.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());group.Add(tail);
            if(source=="terrain_track_infield.fx")
            {
                // This source family needs both detail UVs and lightmap UV2.
                var road=original.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="terrain_road_blend_flat");
                group.Elements("SHADERSTREAMDEFINITION").Remove();
                group.Elements("SHADERGROUPPASS").First().AddBeforeSelf(road.Elements("SHADERSTREAMDEFINITION").Select(e=>new XElement(e)));
                group.SetAttributeValue("parameterStreamCount",road.Attribute("parameterStreamCount")!.Value);
                foreach(var pass in group.Elements("SHADERGROUPPASS"))
                {
                    string mask=pass.Attribute("passConfigMask")!.Value;
                    var rp=road.Elements("SHADERGROUPPASS").Single(p=>(string?)p.Attribute("passConfigMask")==mask);
                    pass.SetAttributeValue("vertexProgram",rp.Attribute("vertexProgram")!.Value);
                }
            }
            foreach(var pass in group.Elements("SHADERGROUPPASS").Where(p=>p.Attribute("passConfigMask")!.Value.Trim() is "00 00 00 01" or "00 00 00 40"))
            {pass.SetAttributeValue("fragmentProgram","#"+alias+".fp");pass.SetAttributeValue("blendEnable",0);}
            var program=new XElement(original.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")==stock+".cg.fp"));program.SetAttributeValue("id",alias+".fp");
            var code=program.Element("SHADERPROGRAMCODE")!;
            string define=Define(source);
            byte[] bytes=SpaShaderCompiler.Compile(define+SpaResources.Text("SpaGround.hlsl"),"ps_5_0");
            code.SetAttributeValue("codeSize",bytes.Length);code.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",bytes.Select(b=>b.ToString("X2")));
            code.Add(Additions());code.SetAttributeValue("parameterCount",code.Elements("SHADERINPUTDEFINITION").Count());
            SpaShaderBindings.Validate(code);
            next.Descendants("LIBRARY").Single(l=>(string?)l.Attribute("type")=="SHADERGROUP").Add(group);
            next.Descendants("LIBRARY").Single(l=>(string?)l.Attribute("type")=="SHADERPROGRAM").Add(program);
            File.WriteAllBytes(Path.Combine(output,alias+".dxbc"),bytes);
            reports.Add(new{Source=source,Alias=alias,ByteCount=bytes.Length,SourceAlbedoMapping=true,SourceBlendMaskChannels=true,SourceMacroColour=true});
        }
        SpaScreens.Shader(original,next,output);
        SpaFoliage.Shader(original,next,output);
        string path=Path.Combine(output,"shaderpack.pssg");var pack=Files.FromDocument(next);
        ValidatePayloads(pack);Files.Save(pack,path);var saved=Files.Pssg(path);ValidatePayloads(saved);
        var check=Files.Document(saved);
        foreach(string source in Sources)
        {
            string id=Alias(source)[..^3]+".fp";var code=check.Descendants("SHADERPROGRAM").Single(e=>(string?)e.Attribute("id")==id).Element("SHADERPROGRAMCODE")!;
            SpaShaderBindings.Validate(code);
            byte[] stored=Convert.FromHexString(string.Concat(code.Element("SHADERPROGRAMCODEBLOCK")!.Value.Where(c=>!char.IsWhiteSpace(c))));
            if(!stored.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output,Alias(source)[..^3]+".dxbc"))))throw new InvalidDataException("Saved shader differs from compiled shader");
        }
        var screenCode=check.Descendants("SHADERPROGRAM").Single(e=>(string?)e.Attribute("id")==SpaScreens.Alias+".fp").Element("SHADERPROGRAMCODE")!;
        byte[] screenBytes=Convert.FromHexString(string.Concat(screenCode.Element("SHADERPROGRAMCODEBLOCK")!.Value.Where(c=>!char.IsWhiteSpace(c))));
        if(!screenBytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output,SpaScreens.Alias+".dxbc"))))throw new InvalidDataException("Saved screen shader changed");
        foreach(var program in check.Descendants("SHADERPROGRAM").Where(e=>((string?)e.Attribute("id"))?.StartsWith(SpaFoliage.Prefix,StringComparison.Ordinal)==true))
        {
            byte[] stored=Convert.FromHexString(string.Concat(program.Descendants("SHADERPROGRAMCODEBLOCK").Single().Value.Where(c=>!char.IsWhiteSpace(c))));
            if(!stored.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output,program.Attribute("id")!.Value+".dxbc"))))throw new InvalidDataException("Saved foliage shader differs");
        }
        foreach(var element in check.Descendants().Where(e=>((string?)e.Attribute("id")) is {} id&&(id.StartsWith("terrain_spa_",StringComparison.Ordinal)||id.StartsWith(SpaScreens.Alias,StringComparison.Ordinal)||id.StartsWith(SpaFoliage.Prefix,StringComparison.Ordinal))).ToArray())element.Remove();
        if(!XNode.DeepEquals(original,check))throw new InvalidDataException("Stock shaders changed");
        Files.Json(Path.Combine(output,"spa-shaders.json"),new{Passed=true,StockHash=Files.Hash(input),OutputHash=Files.Hash(path),AllStockShadersUnchanged=true,Aliases=reports,RuntimeValidated=false});
    }
    static void ValidatePayloads(PssgFile file)
    {
        foreach(var p in file.Elements<PssgElement>().Where(e=>e.Name=="SHADERPROGRAM"))foreach(var c in p.ChildElements.Where(e=>e.Name=="SHADERPROGRAMCODE"))
        {var data=c.ChildElements.Single(e=>e.Name=="SHADERPROGRAMCODEBLOCK").Value;if(data.Length!=c.GetAttributeValue<int>("codeSize"))throw new InvalidDataException("Shader payload truncated");}
    }
}
