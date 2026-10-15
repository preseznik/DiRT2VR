using System.Xml.Linq;
using DiRT2VR.Nordschleife;

internal static class MistyObjectShader
{
    internal const string Name="misty_object_rgb";
    internal static readonly string[] Ids=[Name,Name+".fp",Name+".fp_vn","misty_boat_rgb.fp",Name+".vp"];
    internal static readonly string[] Constants=["MistyMaterial","MistyFresnel","MistySurface","MistyVisibility","MistyLighting"];
    internal static readonly string[] Textures=["TMistySourceDiffuse","TMistyObjectDetail"];
    internal static void Add(XDocument document,string output)
    {
        using var input=typeof(MistyObjectShader).Assembly.GetManifestResourceStream("MistyObject.hlsl")!;
        using var reader=new StreamReader(input);string source=reader.ReadToEnd();
        File.WriteAllText(Path.Combine(output,"MistyObject.hlsl"),source);
        var original=document.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="object_simple_dxt5nm");
        var group=new XElement(original);group.SetAttributeValue("id",Name);
        AddAmbientStream(group);
        foreach(string name in Constants)group.Elements("SHADERINPUTDEFINITION").Last().AddAfterSelf(Definition(name,"constant"));
        foreach(string name in Textures)group.Elements("SHADERINPUTDEFINITION").Last().AddAfterSelf(Definition(name,"texture"));
        group.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        foreach(bool normal in new[]{false,true})
        {
            string suffix=normal?".fp_vn":".fp",id=Name+suffix;
            var donor=document.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")=="object_simple_dxt5nm.cg"+suffix);
            var program=new XElement(donor);program.SetAttributeValue("id",id);
            byte[] bytes=MistyShaderCompiler.Compile((normal?"#define MISTY_NORMAL_PASS\n":"")+source,id);
            var code=program.Element("SHADERPROGRAMCODE")!;
            code.SetAttributeValue("codeSize",bytes.Length);
            code.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",bytes.Select(b=>b.ToString("X2")));
            if(!normal)
            {
                foreach(string name in Constants)code.Add(Definition(name,"constant"));
                foreach(string name in Textures)code.Add(Definition(name,"texture"));
            }
            code.SetAttributeValue("parameterCount",code.Elements("SHADERINPUTDEFINITION").Count());
            donor.Parent!.Add(program);File.WriteAllBytes(Path.Combine(output,id+".dxbc"),bytes);
        }
        string vertex=group.Elements("SHADERGROUPPASS").Single(p=>p.Attribute("passConfigMask")!.Value.Trim()=="00 00 00 01").Attribute("vertexProgram")!.Value;
        using(var vertexInput=typeof(MistyObjectShader).Assembly.GetManifestResourceStream("MistyObjectVertex.hlsl")!)
        using(var vertexReader=new StreamReader(vertexInput))
        {
            string vertexSource=vertexReader.ReadToEnd();
            byte[] bytes=MistyShaderCompiler.Compile(vertexSource,Name+".vp","vs_5_0");
            var program=new XElement(document.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")==vertex.TrimStart('#')));
            program.SetAttributeValue("id",Name+".vp");var code=program.Element("SHADERPROGRAMCODE")!;
            code.Elements("SHADERINPUTDEFINITION").Remove();code.SetAttributeValue("parameterCount",0);code.SetAttributeValue("codeSize",bytes.Length);
            var colour=new XElement(code.Elements("CGSTREAM").Last());
            colour.SetAttributeValue("cgStreamName","COLOR");colour.SetAttributeValue("cgStreamRenderType","Color");
            code.Elements("CGSTREAM").Last().AddAfterSelf(colour);code.SetAttributeValue("streamCount",code.Elements("CGSTREAM").Count());
            MistyShaderCompiler.ValidateStreams(code,bytes);
            code.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",bytes.Select(b=>b.ToString("X2")));
            document.Descendants("SHADERPROGRAM").First().Parent!.Add(program);
            File.WriteAllBytes(Path.Combine(output,Name+".vp.dxbc"),bytes);File.WriteAllText(Path.Combine(output,"MistyObjectVertex.hlsl"),vertexSource);
        }
        foreach(var pass in group.Elements("SHADERGROUPPASS"))
        {
            string mask=pass.Attribute("passConfigMask")!.Value.Trim();
            if(mask=="00 00 00 04")pass.SetAttributeValue("fragmentProgram","#"+Name+".fp_vn");
            if(mask is "00 00 00 01" or "00 00 00 40" or "00 00 00 80")
            {pass.SetAttributeValue("vertexProgram","#"+Name+".vp");pass.SetAttributeValue("fragmentProgram","#"+Name+".fp");}
        }
        original.Parent!.Add(group);
        var boat=document.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="object_animated_ripple");
        foreach(string name in Constants)boat.Elements("SHADERINPUTDEFINITION").Last().AddAfterSelf(Definition(name,"constant"));
        foreach(string name in Textures.Append("TNormalMap"))boat.Elements("SHADERINPUTDEFINITION").Last().AddAfterSelf(Definition(name,"texture"));
        boat.SetAttributeValue("parameterCount",boat.Elements("SHADERINPUTDEFINITION").Count());
        var boatProgram=new XElement(document.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")==Name+".fp"));
        boatProgram.SetAttributeValue("id","misty_boat_rgb.fp");var boatCode=boatProgram.Element("SHADERPROGRAMCODE")!;
        byte[] boatBytes=MistyShaderCompiler.Compile("#define MISTY_BOAT\n"+source,"misty_boat_rgb");
        boatCode.SetAttributeValue("codeSize",boatBytes.Length);boatCode.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",boatBytes.Select(b=>b.ToString("X2")));
        document.Descendants("SHADERPROGRAM").First().Parent!.Add(boatProgram);File.WriteAllBytes(Path.Combine(output,"misty_boat_rgb.fp.dxbc"),boatBytes);
        string boatVertex=boat.Elements("SHADERGROUPPASS").Single(p=>p.Attribute("passConfigMask")!.Value.Trim()=="00 00 00 01").Attribute("vertexProgram")!.Value;
        foreach(var pass in boat.Elements("SHADERGROUPPASS").Where(p=>p.Attribute("passConfigMask")!.Value.Trim() is "00 00 00 01" or "00 00 00 40"))
        {pass.SetAttributeValue("vertexProgram",boatVertex);pass.SetAttributeValue("fragmentProgram","#misty_boat_rgb.fp");}
        foreach(var pass in group.Elements("SHADERGROUPPASS").Concat(boat.Elements("SHADERGROUPPASS")))
        {
            string? ps=(string?)pass.Attribute("fragmentProgram"),vs=(string?)pass.Attribute("vertexProgram");
            if(ps is null||vs is null||!Ids.Contains(ps.TrimStart('#')))continue;
            byte[] Program(string id)=>Convert.FromHexString(string.Concat(document.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")==id.TrimStart('#')).Descendants("SHADERPROGRAMCODEBLOCK").Single().Value.Where(c=>!char.IsWhiteSpace(c))));
            MistyShaderCompiler.ValidateLinkage(Program(vs),Program(ps),ps);
            MistyShaderCompiler.ValidateStreams(pass.Parent!,Program(vs));
        }
        Files.Json(Path.Combine(output,"object-shader-audit.json"),new{OriginalRgbNormals=true,NormalPrepassUpdated=true,StaticVertexAmbient=true,NativeBoatVertexRetained=true,SourceAmbientDiffuseStrengths=true,
            Maps="R: specular strength; G: exponent; B: environment reflectiveness and secondary sun lobe",Detail="Source useDetail/UV multiplier; diffuse and detail alpha follow AC bytecode",
            EnvironmentApproximation="Native ambient and sky response; no AC cubemap",SunVisibility="Conservative whole-mesh visibility, all three fixed suns, native shadow multiplication"});
    }
    internal static void AddAmbientStream(XElement group)
    {
        group.Elements("SHADERSTREAMDEFINITION").Last().AddAfterSelf(new XElement("SHADERSTREAMDEFINITION",new XAttribute("renderTypeName","Color"),new XAttribute("name","COLOR")));
        group.SetAttributeValue("parameterStreamCount",group.Elements("SHADERSTREAMDEFINITION").Count());
    }
    static XElement Definition(string name,string type)=>type=="texture"?new("SHADERINPUTDEFINITION",new XAttribute("name",name),new XAttribute("type",type)):
        new("SHADERINPUTDEFINITION",new XAttribute("name",name),new XAttribute("type",type),new XAttribute("format","float4"));
}
