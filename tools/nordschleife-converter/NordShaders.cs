using System.Xml.Linq;

namespace DiRT2VR.Nordschleife;

internal sealed record VisualBuildOptions(string Game, string Output);

internal static class NordShaders
{
    internal static void Write(Scene scene,string game,string output)
    {
        string source=Files.Inside(game,"dx11/shaderpack/shaderpack.pssg");scene.Inputs[source]=Files.Hash(source);
        var original=Files.Document(Files.Pssg(source));var candidate=new XDocument(original);
        string hlsl=Path.Combine(output,"NordGround.hlsl");
        using(var input=typeof(NordShaders).Assembly.GetManifestResourceStream("NordGround.hlsl")!)
        using(var file=File.Create(hlsl))input.CopyTo(file);
        NordGroundShader.Add(candidate,hlsl,output);
        NordObjectShader.Add(candidate,output);
        Directory.CreateDirectory(Path.Combine(output,"support"));string path=Path.Combine(output,"support/shaderpack.pssg");
        Files.Save(Files.FromDocument(candidate),path);
        NordSceneryShadows.Write(Path.Combine(output,"routesplit.pssg"),path,output);
        var saved=Files.Document(Files.Pssg(path));
        VerifySavedPrograms(saved,output);
        var originalIds=original.Descendants().Attributes("id").Select(a=>a.Value).ToHashSet(StringComparer.Ordinal);
        var additions=saved.Descendants().Where(e=>e.Attribute("id") is {} id&&!originalIds.Contains(id.Value)).ToArray();
        foreach(var item in additions)item.Remove();
        if(!XNode.DeepEquals(original,saved))throw new InvalidDataException("Nordschleife visual shaders changed a stock program or state.");
        Files.Json(Path.Combine(output,"visual-shader-audit.json"),new{Passed=true,StockShadersUnchanged=true,CompiledBytecodeRetained=true,
            AddedIds=additions.Select(e=>(string)e.Attribute("id")!).ToArray(),OriginalHash=Files.Hash(source),RuntimeValidated=false});
    }
    internal static void VerifySavedPrograms(XDocument saved,string output)
    {
        foreach(string name in NordObjectShader.Ids.Skip(1).Append(NordGroundShader.GroundProgram))
        {
            var code=saved.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")==name).Element("SHADERPROGRAMCODE")!;
            byte[] actual=Convert.FromHexString(string.Concat(code.Element("SHADERPROGRAMCODEBLOCK")!.Value.Where(c=>!char.IsWhiteSpace(c))));
            string file=name==NordGroundShader.GroundProgram?NordGroundShader.Ground:name;
            if((int?)code.Attribute("codeSize")!=actual.Length||!actual.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output,file+".dxbc"))))
                throw new InvalidDataException("Saved Nordschleife shader bytecode differs from its compiled program: "+name);
        }
    }
}
