using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal sealed class SpaFoliage
{
    internal const string Prefix="object_spa_leaf_";
    readonly XElement template;
    readonly ShaderInputInfo layout;
    internal static bool IsCard(string source)=>source is "facing_foliage.fx" or "treesheet_foliage.fx";
    internal SpaFoliage(string dirt,Dictionary<string,string> inputs)
    {
        string path=Files.Inside(dirt,"tracks/london/battersea/objects.pssg");inputs.TryAdd(path,Files.Hash(path));
        var file=Files.Pssg(path);var xml=new XElement("root");file.GetObject<PssgShaderGroup>("object_simple.fx".AsMemory()).WriteXml(xml);
        template=xml.Elements().Single();
        // ST1 carries geometry in metres; preserve source precision instead of
        // rounding the leaf expansion and atlas into object_simple's half2 UVs.
        layout=new("object_simple.fx",[new ShaderBlockInputInfo([
            new("Vertex","float3",0,40),new("ST","float2",12,40),new("ST","float2",20,40),new("Normal","float3",28,40)])]);
    }
    internal NativeMaterial Convert(string source,Dictionary<string,string> textures)
    {
        string alias=Prefix+(source=="facing_foliage.fx"?"facing":"sheet")+".fx";
        var group=new XElement(template);group.SetAttributeValue("id",alias);
        if(!group.Elements("SHADERINPUTDEFINITION").Any(d=>(string?)d.Attribute("name")=="preBakedShadows"))
            group.Add(new XElement("SHADERINPUTDEFINITION",new XAttribute("name","preBakedShadows"),new XAttribute("type","constant"),new XAttribute("format","float")));
        group.SetAttributeValue("parameterCount",group.Elements("SHADERINPUTDEFINITION").Count());
        var definitions=group.Elements("SHADERINPUTDEFINITION").ToArray();
        var instance=new XElement("SHADERINSTANCE",new XAttribute("id","spa_leaf"),new XAttribute("shaderGroup","#"+alias),new XAttribute("parameterCount",definitions.Length),new XAttribute("renderSortPriority",0));
        void Set(string name,string value)
        {
            int index=Array.FindIndex(definitions,d=>(string?)d.Attribute("name")==name);var d=definitions[index];
            var i=new XElement("SHADERINPUT",new XAttribute("parameterID",index),new XAttribute("type",d.Attribute("type")!.Value));
            if(d.Attribute("format") is {} format)i.SetAttributeValue("format",format.Value);
            if((string?)d.Attribute("type")=="texture")i.SetAttributeValue("texture",value);else i.Value=value;
            instance.Add(i);
        }
        foreach(string name in new[]{"TDiffuseAlphaMap","TSpecularMap","TOcclusionMap"})
        {Set(name,name);textures[name]=name=="TDiffuseAlphaMap"?textures["txDiffuse"]:name=="TOcclusionMap"?"nord_white":"nord_black";}
        Set("preBakedShadows","0");instance.SetAttributeValue("parameterSavedCount",instance.Elements().Count());
        return new(group,instance,new(alias,layout.BlockInputs),false);
    }
    internal static float OffsetScale(string source,Matrix4x4 world)
    {
        if(source!="facing_foliage.fx")return 1;
        var x=new Vector3(world.M11,world.M12,world.M13);var y=new Vector3(world.M21,world.M22,world.M23);var z=new Vector3(world.M31,world.M32,world.M33);
        float scale=x.Length();
        if(MathF.Abs(y.Length()-scale)>.001f||MathF.Abs(z.Length()-scale)>.001f||MathF.Abs(Vector3.Dot(x,y))>.001f||MathF.Abs(Vector3.Dot(x,z))>.001f||MathF.Abs(Vector3.Dot(y,z))>.001f)
            throw new InvalidDataException("Facing foliage must have a uniform source scale");
        return scale;
    }
    internal static Bounds[] ExpandBounds(Scene scene,string output)
    {
        string path=Path.Combine(output,"routesplit.pssg");var file=Files.Pssg(path);int vertices=0,triangles=0;
        var expected=new Dictionary<(string,Vector3),HashSet<(Vector2,Vector2)>>();
        foreach(var mesh in scene.Visuals.Where(m=>((string?)m.Material.Native?.Group.Attribute("id"))?.StartsWith(Prefix,StringComparison.Ordinal)==true))
        for(int i=0;i<mesh.Positions.Length;i++)
        {
            var key=((string)mesh.Material.Native!.Group.Attribute("id")!,mesh.Positions[i]);
            if(!expected.TryGetValue(key,out var uv))expected[key]=uv=[];
            uv.Add((mesh.TexCoords![0][i],mesh.TexCoords[1][i]));
        }
        var matched=new HashSet<(string,Vector3)>();float maxUvError=0;
        var roots=file.Elements<PssgNode>().Where(n=>n.Id.StartsWith("ROOT_",StringComparison.Ordinal)).Reverse().ToArray();
        foreach(var root in roots)
        {
            var bound=new Bounds(root.BoundingBox.BoundsMin,root.BoundingBox.BoundsMax);
            foreach(var draw in root.ChildElements.OfType<PssgRenderNode>().Where(n=>n.Id.StartsWith("HIGH_",StringComparison.Ordinal)).SelectMany(n=>n.ChildElements.OfType<PssgRenderStreamInstance>()))
            {
                string family=draw.GetShaderInstance().GetShaderGroup().Id;
                if(!family.StartsWith(Prefix,StringComparison.Ordinal))continue;
                var reader=new RenderDataSourceReader(draw.GetRenderDataSource());triangles+=(int)reader.IndexCount/3;
                for(uint i=0;i<reader.VertexCount;i++)
                {
                    var p=reader.GetPosition(i);var key=(family,p);
                    if(!expected.TryGetValue(key,out var uv))throw new InvalidDataException("Exported card centre differs from source: "+key);
                    var actualUV=reader.GetTexCoord(i,0);var actualOffset=reader.GetTexCoord(i,1);
                    float error=uv.Min(v=>Math.Max(Vector2.Distance(v.Item1,actualUV),Vector2.Distance(v.Item2,actualOffset)));
                    // The writer flips V into native storage and the reader flips
                    // it back; two float32 subtractions can round near zero.
                    if(error>2e-6f)throw new InvalidDataException($"Exported card UV/offset differs: {key}, {actualUV}, {actualOffset}, source {string.Join(';',uv)}, error {error}");
                    maxUvError=Math.Max(maxUvError,error);
                    matched.Add(key);var radius=new Vector3(reader.GetTexCoord(i,1).Length());bound=bound.Union(new(p-radius,p+radius));vertices++;
                }
            }
            root.BoundingBox.BoundsMin=bound.Min;root.BoundingBox.BoundsMax=bound.Max;
            foreach(var child in root.ChildElements.OfType<PssgNode>()){child.BoundingBox.BoundsMin=bound.Min;child.BoundingBox.BoundsMax=bound.Max;}
        }
        if(matched.Count!=expected.Count)throw new InvalidDataException("Source foliage card centres missing");
        foreach(var parent in file.Elements<PssgNode>().Reverse())
        foreach(var child in parent.ChildElements.OfType<PssgNode>())
        {parent.BoundingBox.BoundsMin=Vector3.Min(parent.BoundingBox.BoundsMin,child.BoundingBox.BoundsMin);parent.BoundingBox.BoundsMax=Vector3.Max(parent.BoundingBox.BoundsMax,child.BoundingBox.BoundsMax);}
        using(var stream=new FileStream(path,FileMode.Truncate))file.Save(stream);
        Files.Json(Path.Combine(output,"foliage.json"),new{Passed=true,CardVertices=vertices,CardTriangles=triangles,SourceCentres=matched.Count,ExportedCentresExact=true,MaximumAtlasAndOffsetRoundTripError=maxUvError,Float32UVRoundTripTolerance=2e-6,AllCameraOrientationsInsideExpandedTileBounds=true,FacingOffsetsUseSourceScaleOnce=true,SheetOffsetsInWorldMetres=true,SourceAtlasAlphaPreserved=true,WindReproduced=false,RuntimeValidated=false});
        return roots.Select(r=>new Bounds(r.BoundingBox.BoundsMin,r.BoundingBox.BoundsMax)).ToArray();
    }
    internal static void Shader(XDocument original,XDocument next,string output)
    {
        string hlsl=SpaResources.Text("SpaFoliage.hlsl");
        foreach(string kind in new[]{"facing","sheet"})
        {
            string alias=Prefix+kind,define=kind=="facing"?"#define SPA_FACING\n":"";
            var group=new XElement(original.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="object_simple"));group.SetAttributeValue("id",alias);
            var streams=group.Elements("SHADERSTREAMDEFINITION").ToArray();
            foreach(var pass in group.Elements("SHADERGROUPPASS"))
            {
                string mask=pass.Attribute("passConfigMask")!.Value.Trim();
                string mode=mask switch{"00 00 00 02"=>"velocity","00 00 00 04"=>"normal_depth","00 00 00 08"=>"shadow",_=>"colour"};
                pass.SetAttributeValue("vertexProgram","#"+alias+"."+mode+".vp");pass.SetAttributeValue("cullFaceType","NONE");
                if(mode=="shadow")pass.SetAttributeValue("fragmentProgram","#"+Prefix+"shadow.fp");
            }
            next.Descendants("LIBRARY").Single(l=>(string?)l.Attribute("type")=="SHADERGROUP").Add(group);
            foreach(string mode in new[]{"colour","velocity","normal_depth","shadow"})
            {
                var program=new XElement(original.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")=="object_simple.object_simple.cg.vp"));
                string id=alias+"."+mode+".vp";program.SetAttributeValue("id",id);var code=program.Element("SHADERPROGRAMCODE")!;
                byte[] bytes=SpaShaderCompiler.Compile(define+"#define SPA_"+mode.ToUpperInvariant()+"\n"+hlsl,"vs_5_0");
                code.RemoveNodes();code.Add(new XElement("SHADERPROGRAMCODEBLOCK",string.Join(" ",bytes.Select(b=>b.ToString("X2")))));
                foreach(var stream in streams)code.Add(new XElement("CGSTREAM",new XAttribute("cgStreamName",stream.Attribute("name")!.Value),new XAttribute("cgStreamDataType","streamCount"),new XAttribute("cgStreamRenderType",stream.Attribute("renderTypeName")!.Value)));
                // DXBC retains all seven members of the native $Globals layout
                // when prevViewProj is used. Declare the complete reflected
                // layout, including its unused members, as stock programs do.
                if(mode is "velocity" or "normal_depth")
                    code.Add(original.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")=="object_simple.object_simple.cg.vp").Element("SHADERPROGRAMCODE")!.Elements("SHADERINPUTDEFINITION").Select(d=>new XElement(d)));
                code.SetAttributeValue("streamCount",streams.Length);code.SetAttributeValue("parameterCount",code.Elements("SHADERINPUTDEFINITION").Count());code.SetAttributeValue("codeSize",bytes.Length);
                next.Descendants("LIBRARY").Single(l=>(string?)l.Attribute("type")=="SHADERPROGRAM").Add(program);
                File.WriteAllBytes(Path.Combine(output,id+".dxbc"),bytes);
            }
        }
        var shadow=new XElement(original.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")=="object_simple.cg.fp"));
        shadow.SetAttributeValue("id",Prefix+"shadow.fp");var sc=shadow.Element("SHADERPROGRAMCODE")!;
        byte[] ps=SpaShaderCompiler.Compile("Texture2D TDiffuseAlphaMap:register(t0); SamplerState diffuseSampler:register(s0); float4 main(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {clip(TDiffuseAlphaMap.Sample(diffuseSampler,uv).a-.5);return 0;}","ps_5_0");
        sc.RemoveNodes();sc.Add(new XElement("SHADERPROGRAMCODEBLOCK",string.Join(" ",ps.Select(b=>b.ToString("X2")))),new XElement("SHADERINPUTDEFINITION",new XAttribute("name","TDiffuseAlphaMap"),new XAttribute("type","texture")));
        sc.SetAttributeValue("parameterCount",1);sc.SetAttributeValue("streamCount",0);sc.SetAttributeValue("codeSize",ps.Length);
        next.Descendants("LIBRARY").Single(l=>(string?)l.Attribute("type")=="SHADERPROGRAM").Add(shadow);File.WriteAllBytes(Path.Combine(output,Prefix+"shadow.fp.dxbc"),ps);
    }
}

