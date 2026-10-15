using System.Security.Cryptography;
using System.Xml.Linq;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

namespace DiRT2VR.Nordschleife;

// Imported scenery lives in terrain colour lists. EGO submits shadow casters
// from the corresponding batch lists, which previously contained only terrain.
internal static class NordSceneryShadows
{
    static readonly string[] Sources=[NordObjectShader.Name];
    static string Name(string source)=>"nord_shadow_"+source;

    internal static void Write(string routePath,string shaderPath,string auditDirectory)
    {
        var route=Files.Pssg(routePath);
        var original=Files.Document(Files.Pssg(shaderPath));
        var shaders=new XDocument(original);
        var donor=shaders.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="object_simple_mesh");
        var shadow=new XElement(donor.Elements("SHADERGROUPPASS").Single(p=>p.Attribute("passConfigMask")!.Value.Trim()=="00 00 00 08"));
        if((string?)shadow.Attribute("fragmentProgram")!="#csm_depth_opaque.psh.mainAlphaTested")
            throw new InvalidDataException("Unexpected native alpha-tested shadow program.");
        const string vertexId="nord_scenery_shadow.vp";
        const string vertexSource="""
            cbuffer CameraParamsConstantBuffer : register(b3) {
                float4x4 view, projection, viewProjection, viewI, projectionI, viewIT;
                float4 eyePositionWS;
            };
            struct Vertex {float3 position:POSITION0;float3 normal:NORMAL0;float2 uv:TEXCOORD0;};
            struct Pixel {float4 position:SV_Position;float2 uv:TEXCOORD1;};
            Pixel main(Vertex i) {
                Pixel o;
                // Terrain batch vertices are already in world space. Its CSM
                // combined matrix includes the native adjusted depth range.
                o.position=mul(viewProjection,float4(i.position,1));o.uv=i.uv;return o;
            }
            """;
        byte[] vertexBytes=NordShaderCompiler.Compile(vertexSource,vertexId,"vs_5_0");
        var program=new XElement(shaders.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")==shadow.Attribute("vertexProgram")!.Value[1..]));
        program.SetAttributeValue("id",vertexId);var code=program.Element("SHADERPROGRAMCODE")!;
        code.Elements("SHADERINPUTDEFINITION").Remove();code.SetAttributeValue("parameterCount",0);code.SetAttributeValue("codeSize",vertexBytes.Length);
        code.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",vertexBytes.Select(b=>b.ToString("X2")));
        shaders.Descendants("LIBRARY").Single(e=>(string?)e.Attribute("type")=="SHADERPROGRAM").Add(program);
        shadow.SetAttributeValue("vertexProgram","#"+vertexId);
        var added=new HashSet<string>();
        string before=Hash(route.RootElement,added);
        var materials=new Dictionary<string,string>();
        var groups=new List<string>();
        foreach(string source in Sources)
        {
            var routeGroup=route.Elements<PssgShaderGroup>().SingleOrDefault(g=>g.Id==source+".fx");
            if(routeGroup is null)continue;
            string name=Name(source);
            var group=new XElement(shaders.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")==source));
            // Shadow-only batches do not consume the new ambient colour stream.
            // Retain their accepted stream layout independently of the colour pass.
            if(source==NordObjectShader.Name){group.Elements("SHADERSTREAMDEFINITION").Where(e=>(string?)e.Attribute("renderTypeName")=="Color").Remove();group.SetAttributeValue("parameterStreamCount",group.Elements("SHADERSTREAMDEFINITION").Count());}
            group.SetAttributeValue("id",name);
            group.Elements("SHADERGROUPPASS").Remove();group.Elements("LAYER").Remove();
            group.Add(new XElement(shadow));group.SetAttributeValue("passCount",1);
            shaders.Descendants("LIBRARY").Single(e=>(string?)e.Attribute("type")=="SHADERGROUP").Add(group);
            groups.Add(name);
            var copy=(PssgShaderGroup)routeGroup.DeepClone();copy.Id=name+".fx";
            routeGroup.ParentElement!.AppendChild(copy);added.Add(copy.Id);
            foreach(var material in route.Elements<PssgShaderInstance>().Where(m=>m.ShaderGroup=="#"+routeGroup.Id).ToArray())
            {
                var instance=(PssgShaderInstance)material.DeepClone();instance.Id=material.Id+"_nord_shadow";
                instance.ShaderGroup="#"+copy.Id;material.ParentElement!.AppendChild(instance);
                added.Add(instance.Id);materials.Add("#"+material.Id,"#"+instance.Id);
            }
        }
        int draws=0,alreadyOpaque=0,sourceExcluded=0;
        foreach(var node in route.Elements<PssgRenderNode>().Where(n=>n.Id.StartsWith("HIGH_",StringComparison.Ordinal)||n.Id.StartsWith("LOW_",StringComparison.Ordinal)).ToArray())
        {
            var batch=node.ParentElement!.ChildElements.OfType<PssgRenderNode>().Single(n=>n.Id==node.Id.Insert(node.Id.IndexOf('_'),"BATCH"));
            var existing=batch.RenderInstances.Select(d=>d.Indices).ToHashSet();
            foreach(var draw in node.RenderInstances.Where(d=>materials.ContainsKey(d.Shader)).ToArray())
            {
                var sourceMaterial=draw.GetShaderInstance();
                var definitions=sourceMaterial.GetShaderGroup().InputDefinitions.ToArray();
                int lighting=Array.FindIndex(definitions,d=>d.InputName=="NordLighting");
                byte[] flags=sourceMaterial.Inputs.Single(i=>i.ParameterId==lighting).Value;
                if(flags.Length!=16)throw new InvalidDataException("Invalid Nord shadow flags.");
                if(System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(flags.AsSpan(12,4))==0){sourceExcluded++;continue;}
                // Some simple opaque props already use the position-only batch.
                if(existing.Contains(draw.Indices.Replace("#nord_","#nord_depth_",StringComparison.Ordinal))){alreadyOpaque++;continue;}
                var copy=(PssgRenderStreamInstance)draw.DeepClone();copy.Id=draw.Id+"_nord_shadow";
                copy.Shader=materials[draw.Shader];batch.AppendChild(copy);added.Add(copy.Id);draws++;
            }
        }
        if(draws==0||groups.Count==0)throw new InvalidDataException("No missing Nord scenery shadow submissions found.");
        if(route.Elements<PssgObject>().GroupBy(e=>e.Id).Any(g=>g.Count()>1))throw new InvalidDataException("Duplicate scenery shadow identity.");
        string routeCandidate=routePath+".shadow",shaderCandidate=shaderPath+".shadow";
        Files.Save(route,routeCandidate);Files.Save(Files.FromDocument(shaders),shaderCandidate);
        var savedRoute=Files.Pssg(routeCandidate);var savedShaders=Files.Document(Files.Pssg(shaderCandidate));
        var savedProgram=savedShaders.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")==vertexId);
        byte[] readback=Convert.FromHexString(string.Concat(savedProgram.Descendants("SHADERPROGRAMCODEBLOCK").Single().Value.Where(c=>!char.IsWhiteSpace(c))));
        if(!readback.AsSpan().SequenceEqual(vertexBytes))throw new InvalidDataException("Scenery shadow vertex program changed on serialization.");
        savedProgram.Remove();
        foreach(string name in groups)
        {
            var group=savedShaders.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")==name);
            if(group.Elements("SHADERGROUPPASS").Count()!=1||!XNode.DeepEquals(group.Elements("SHADERGROUPPASS").Single(),shadow))
                throw new InvalidDataException("Scenery shadow pass changed on serialization.");
            group.Remove();
        }
        if(!XNode.DeepEquals(original,savedShaders))throw new InvalidDataException("Existing shaders changed while adding scenery shadows.");
        if(Hash(savedRoute.RootElement,added)!=before||savedRoute.Elements<PssgObject>().Count(e=>added.Contains(e.Id))!=added.Count)
            throw new InvalidDataException("Existing route data changed while adding scenery shadows.");
        File.Move(routeCandidate,routePath,true);File.Move(shaderCandidate,shaderPath,true);
        File.WriteAllText(Path.Combine(auditDirectory,"scenery-shadow.hlsl"),vertexSource);
        File.WriteAllBytes(Path.Combine(auditDirectory,"scenery-shadow.dxbc"),vertexBytes);
        Files.Json(Path.Combine(auditDirectory,"scenery-shadow-audit.json"),new{Passed=true,ShadowOnlyDraws=draws,AlreadyOpaqueDraws=alreadyOpaque,SourceExcludedDraws=sourceExcluded,
            Groups=groups,Materials=materials.Count,ExistingRouteDataExact=true,ExistingShadersExact=true,GeometryAndTexturesUnchanged=true,
            NativeAlphaCutoff=.5,WorldSpaceTerrainProjection=true,SourceCastShadowFlagsRetained=true,RuntimeValidated=false,NewIds=added.Order().ToArray(),ExistingRouteDataHash=before});
    }

    static string Hash(PssgElement root,HashSet<string> excluded)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Text(string value)=>hash.AppendData(System.Text.Encoding.UTF8.GetBytes(value+"\0"));
        void Walk(PssgElement element)
        {
            if(element is PssgObject obj&&excluded.Contains(obj.Id))return;
            Text(element.Name);foreach(var a in element.Attributes){Text(a.Name);Text(a.DisplayValue);}
            hash.AppendData(element.Value);foreach(var child in element.ChildElements)Walk(child);Text("end");
        }
        Walk(root);return Convert.ToHexString(hash.GetHashAndReset());
    }
}
