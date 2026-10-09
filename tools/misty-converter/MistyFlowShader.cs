using System.Xml.Linq;
using DiRT2VR.Nordschleife;

internal static class MistyFlowShader
{
    // The native time updater recognizes this identity; a new group name renders
    // the bytecode but leaves scrolling frozen. This patch is isolated to the lab.
    internal const string Name="flag_scrolling";
    internal static void Write(Scene scene,string game,string output)
    {
        string source=Files.Inside(game,"dx11/shaderpack/shaderpack.pssg");scene.Inputs[source]=Files.Hash(source);
        var original=Files.Document(Files.Pssg(source));var candidate=new XDocument(original);
        var stock=candidate.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="flag_scrolling");
        if((int?)stock.Attribute("passCount")!=6)throw new InvalidDataException("Unexpected stock scrolling shader");
        var originalGroup=new XElement(stock);
        var flow=new XElement(stock);flow.SetAttributeValue("id",Name);
        flow.SetAttributeValue("instancesRequireSorting",1);flow.SetAttributeValue("defaultRenderSortPriority",-536870912);
        foreach(var pass in flow.Elements("SHADERGROUPPASS").ToArray())
        {
            string mask=pass.Attribute("passConfigMask")!.Value.Trim();
            // Foam blends after opaque scenery; it must not write opaque depth or shadows.
            if(mask is not ("00 00 00 01" or "00 00 00 40" or "00 00 00 80")){pass.Remove();continue;}
            pass.SetAttributeValue("blendEnable",1);pass.SetAttributeValue("blendSource","SRC_ALPHA");
            pass.SetAttributeValue("blendDest","ONE_MINUS_SRC_ALPHA");pass.SetAttributeValue("alphaTestEnable",0);
            pass.SetAttributeValue("alphaTestFunc","ALWAYS");pass.SetAttributeValue("alphaTestRef","0.000000000e+000");
            pass.SetAttributeValue("depthMaskEnable",0);
        }
        flow.SetAttributeValue("passCount",flow.Elements("SHADERGROUPPASS").Count());stock.ReplaceWith(flow);
        string shaderSource=Path.Combine(output,"MistyDetail.hlsl");
        using(var input=typeof(MistyFlowShader).Assembly.GetManifestResourceStream("MistyDetail.hlsl")!)
        using(var stream=File.Create(shaderSource))input.CopyTo(stream);
        MistyDetailShader.Add(candidate,shaderSource,output);
        Directory.CreateDirectory(Path.Combine(output,"support"));string path=Path.Combine(output,"support/shaderpack.pssg");
        Files.Save(Files.FromDocument(candidate),path);
        var saved=Files.Document(Files.Pssg(path));
        foreach(string name in new[]{MistyDetailShader.Ground,MistyDetailShader.Grass})
        {
            var code=saved.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")==name+".fp").Element("SHADERPROGRAMCODE")!;
            var actual=Convert.FromHexString(string.Concat(code.Element("SHADERPROGRAMCODEBLOCK")!.Value.Where(c=>!char.IsWhiteSpace(c))));
            if(!actual.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output,name+".dxbc")))||(int)code.Attribute("codeSize")! != actual.Length)
                throw new InvalidDataException("Saved detail shader bytecode differs from its compiled program.");
        }
        var savedFlow=saved.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")==Name);
        if(!XNode.DeepEquals(flow,savedFlow))throw new InvalidDataException("Flow shader state changed in serialization");
        savedFlow.ReplaceWith(originalGroup);
        foreach(var item in saved.Descendants().Where(e=>(string?)e.Attribute("id") is MistyDetailShader.Ground or MistyDetailShader.Grass or MistyDetailShader.GroundProgram or MistyDetailShader.GrassProgram).ToArray())item.Remove();
        if(!XNode.DeepEquals(original,saved))throw new InvalidDataException("Stock shader programs or states changed");
        Files.Json(Path.Combine(output,"flow-shader-audit.json"),new{Passed=true,ModifiedGroup=Name,OriginalHash=Files.Hash(source),OtherStockShadersAndStatesUnchanged=true,OriginalBytecodeRetained=true,SoftSourceAlpha=true,NoDepthOrShadowPass=true,NativeClockIdentityRetained=true,LabOnly=true,RuntimeValidated=false});
    }
}
