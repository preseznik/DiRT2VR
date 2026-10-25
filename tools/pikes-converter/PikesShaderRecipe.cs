using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Only conversion instructions and authored shader changes are embedded. Native
// programs and source grading pixels are read from the user's verified games.
internal static class PikesShaderRecipe
{
    internal static void Write(string game,string rally,string layout,string output,bool grading=false,bool performanceAssets=false)
    {
        string name=grading?"postprocess":"shaderpack";
        string input=Files.Inside(game,"dx11/shaderpack/"+name+".pssg");
        var recipe=XDocument.Parse(PikesResources.Text(grading?layout+"-grading.xml":layout=="gravel-alt"?"gravel-alt-shaders.xml":"common-shaders.xml")).Root!;
        if(Files.Hash(input)!=(string?)recipe.Attribute("sourceSha256"))
            throw new InvalidDataException("Original DiRT 2 shader file changed: "+name);
        var original=Files.Document(Files.Pssg(input));
        var paths=new Dictionary<string,XElement>();
        Index(original.Root!,"");
        var palettes=new Dictionary<string,byte[]>();
        var generated=new XDocument(Expand(recipe.Elements().Single()));
        if(!grading)
        {
            var program=generated.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")=="pikes_helicopter51.vp").Element("SHADERPROGRAMCODE")!;
            byte[] flight=PikesHelicopters.Program(rally);
            program.SetAttributeValue("codeSize",flight.Length);
            program.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",flight.Select(b=>b.ToString("X2",CultureInfo.InvariantCulture)));
            if(performanceAssets)PikesTreeBillboards.Shaders(generated);
        }
        var file=Files.FromDocument(generated);PikesShaderPayload45.Validate(file);
        Files.Save(file,output);
        XElement Expand(XElement node)
        {
            if(node.Name=="copy")return new XElement(Reference(node));
            if(node.Name=="patch")
            {
                var source=Reference(node);byte[] bytes=Convert.FromHexString(string.Concat(source.Value.Where(c=>!char.IsWhiteSpace(c))));
                using var result=new MemoryStream();
                foreach(var part in node.Elements())
                {
                    if(part.Name=="take")
                    {
                        int offset=int.Parse((string)part.Attribute("offset")!,CultureInfo.InvariantCulture),count=int.Parse((string)part.Attribute("bytes")!,CultureInfo.InvariantCulture);
                        result.Write(bytes.AsSpan(offset,count));
                    }
                    else if(part.Name=="put")result.Write(Convert.FromHexString(part.Value));
                    else if(part.Name=="palette")
                    {
                        string id=(string)part.Attribute("id")!;
                        if(!palettes.TryGetValue(id,out var pixels))
                        {
                            var library=PikesSponsorBarrier.Read(Files.Inside(rally,"postprocess/palettes.pssg"));
                            var texture=library.GetObject<PssgTexture>(id.AsMemory());
                            using var dds=new MemoryStream();texture.ToDdsFile().Write(dds);
                            if(dds.Length!=32896)throw new InvalidDataException("Unexpected source grading cube: "+id);
                            pixels=dds.ToArray()[128..];palettes.Add(id,pixels);
                        }
                        if(Hash(pixels)!=(string?)part.Attribute("sha256"))throw new InvalidDataException("Source grading cube changed: "+id);
                        result.Write(pixels);
                    }
                    else throw new InvalidDataException("Unknown shader recipe operation");
                }
                byte[] final=result.ToArray();
                if(Hash(final)!=(string?)node.Attribute("sha256"))throw new InvalidDataException("Shader conversion verification failed");
                return new XElement(source.Name,source.Attributes(),string.Join(" ",final.Select(b=>b.ToString("X2",CultureInfo.InvariantCulture))));
            }
            var next=new XElement(node.Name,node.Attributes());
            if(node.HasElements)foreach(var child in node.Elements())next.Add(Expand(child));else next.Value=node.Value;
            return next;
        }
        XElement Reference(XElement node)=>paths[(string)node.Attribute("path")!];
        void Index(XElement node,string path)
        {
            paths.Add(path,node);int i=0;foreach(var child in node.Elements())Index(child,path+"/"+i++);
        }
    }
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
