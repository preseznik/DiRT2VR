using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class PikesTreeBillboards
{
    internal const string Name="pikes_tree_billboard";
    internal const float Distance=200;
    static readonly (string Semantic,string Type)[] Streams=[("POSITION","Vertex"),("TEXCOORD0","ST"),("TEXCOORD1","ST"),("TEXCOORD2","ST"),("NORMAL","Normal"),("TANGENT","Tangent")];
    static readonly ShaderInputInfo Layout=new(Name+".fx",[new ShaderBlockInputInfo([
        new("Vertex","float3",0,60),new("ST","float2",12,60),new("ST","float2",20,60),new("ST","float2",28,60),new("Normal","float3",36,60),new("Tangent","float3",48,60)])]);

    internal static Mesh? Geometry(PssgRenderStreamInstance draw,Matrix4x4 world,Vector3 origin,string label)
    {
        if(draw.GetShaderInstance().GetShaderGroup().Id!="foliage_billboard.fx")return null;
        var r=new RenderDataSourceReader(draw.GetRenderDataSource());
        if(r.VertexCount!=4||r.IndexCount!=6||r.TexCoordSetCount!=2||r.Primitive!="triangles")return null;
        var x=Vector3.TransformNormal(Vector3.UnitX,world);var y=Vector3.TransformNormal(Vector3.UnitY,world);var z=Vector3.TransformNormal(Vector3.UnitZ,world);
        float sx=x.Length(),sy=y.Length(),sz=z.Length();
        if(!float.IsFinite(sx+sy+sz)||Math.Min(sx,Math.Min(sy,sz))<.0001f||world.GetDeterminant()<=0||Math.Abs(sx-sz)>Math.Max(sx,sz)*.001f)return null;
        x/=sx;y/=sy;z/=sz;
        if(Math.Abs(Vector3.Dot(x,y))+Math.Abs(Vector3.Dot(x,z))+Math.Abs(Vector3.Dot(y,z))>.003f)return null;
        var positions=Enumerable.Range(0,4).Select(i=>Vector3.Transform(r.GetPosition((uint)i),world)-origin).ToArray();
        if(positions.Any(p=>Vector3.DistanceSquared(p,positions[0])>.0001f))return null;
        var uv=Enumerable.Range(0,4).Select(i=>r.GetTexCoord((uint)i,0)).ToArray();
        if(Math.Abs(uv.Max(p=>p.X)-uv.Min(p=>p.X)-.125f)>.0001f)return null;
        var offsets=Enumerable.Range(0,4).Select(i=>r.GetTexCoord((uint)i,1)*new Vector2(sx,sy)).ToArray();
        if(offsets.Any(o=>!float.IsFinite(o.X+o.Y)))return null;
        var definitions=draw.GetShaderInstance().GetShaderGroup().InputDefinitions.ToArray();
        foreach(var input in draw.GetShaderInstance().Inputs.Where(i=>definitions[i.ParameterId].InputName is "ScaleX_solo" or "ScaleY_solo"))
            if(input.Value.Length!=4||BinaryPrimitives.ReadSingleBigEndian(input.Value)!=1)return null;
        return new(label+"_billboard",null!,false,positions,Enumerable.Repeat(y,4).ToArray(),uv,Enumerable.Repeat(x,4).ToArray(),
            Enumerable.Range(0,6).Select(i=>checked((ushort)r.GetIndex(i))).ToArray()) {TexCoords=[uv,offsets,new Vector2[4]],LodIn=Distance};
    }

    internal static void Materials(Scene scene)
    {
        var prototype=scene.Visuals.First(m=>m.Material.Native?.Group.Attribute("id")?.Value=="pikes_canopy_facing.fx").Material;
        var definitions=prototype.Native!.Group.Elements("SHADERINPUTDEFINITION").ToArray();
        var original=prototype.Native.Instance.Elements("SHADERINPUT").ToDictionary(i=>(string)definitions[(int)i.Attribute("parameterID")!].Attribute("name")!);
        var contracts=XDocument.Parse(PikesResources.Text("canopies.xml"));
        var group=new XElement(contracts.Root!.Elements("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="pikes_canopy_sheet.fx"));
        group.SetAttributeValue("id",Name+".fx");
        var converted=new Dictionary<string,Material>();
        for(int i=0;i<scene.Visuals.Count;i++)
        {
            var mesh=scene.Visuals[i];if(mesh.Material.Shader!=Name)continue;
            if(!converted.TryGetValue(mesh.Material.Id,out var material))
            {
                var maps=new Dictionary<string,string>(prototype.Textures){["txDiffuse"]=mesh.Material.Textures["txDiffuse"],
                    ["TDiffuseAlphaMap"]=mesh.Material.Textures["txDiffuse"],["TNormalMap"]=mesh.Material.Textures["TNormalMap"]};
                var instance=new XElement("SHADERINSTANCE",new XAttribute("id",mesh.Material.Id),new XAttribute("shaderGroup","#"+Name+".fx"),new XAttribute("parameterCount",group.Elements().Count()),new XAttribute("renderSortPriority",0));
                int parameter=0;
                foreach(var definition in group.Elements("SHADERINPUTDEFINITION"))
                {
                    string name=(string)definition.Attribute("name")!;
                    if(original.TryGetValue(name,out var input)){var copy=new XElement(input);copy.SetAttributeValue("parameterID",parameter);if((string?)copy.Attribute("type")=="texture")copy.SetAttributeValue("texture",name);instance.Add(copy);}
                    parameter++;
                }
                instance.SetAttributeValue("parameterSavedCount",instance.Elements().Count());
                material=mesh.Material with{Textures=maps,Native=new(group,instance,Layout,false),AlphaTest=true};converted.Add(material.Id,material);
            }
            scene.Visuals[i]=mesh with{Material=material};
        }
    }

    internal static void Shaders(XDocument pack)
    {
        var source=pack.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="pikes_canopy_sheet");
        var group=new XElement(source);group.SetAttributeValue("id",Name);
        group.Elements("SHADERSTREAMDEFINITION").Remove();
        group.Elements("SHADERINPUTDEFINITION").Last().AddAfterSelf(Streams.Select(s=>new XElement("SHADERSTREAMDEFINITION",new XAttribute("name",s.Semantic),new XAttribute("renderTypeName",s.Type))));
        group.SetAttributeValue("parameterStreamCount",Streams.Length);
        var programs=new Dictionary<string,byte[]>();
        foreach(var (suffix,defines,vertex) in new[]{("vp","",true),("shadow.vp","#define BILLBOARD_SHADOW\n",true),
            ("fp","",false),("depth.fp","#define BILLBOARD_DEPTH\n",false),("normal.fp","#define BILLBOARD_NORMAL\n",false)})
        {
            byte[] bytes=PikesShaderCompiler.Compile(defines+PikesResources.Text(vertex?"TreeBillboardVertex.hlsl":"TreeBillboardPixel.hlsl"),vertex?"vs_5_0":"ps_5_0");
            string id=Name+"."+suffix;programs[id]=bytes;
            var program=new XElement(pack.Descendants("SHADERPROGRAM").Single(p=>(string?)p.Attribute("id")=="pikes_canopy_sheet."+(vertex?"vp":"fp")));
            program.SetAttributeValue("id",id);var code=program.Element("SHADERPROGRAMCODE")!;
            code.SetAttributeValue("codeSize",bytes.Length);code.Element("SHADERPROGRAMCODEBLOCK")!.Value=string.Join(" ",bytes.Select(b=>b.ToString("X2",CultureInfo.InvariantCulture)));
            code.Elements("CGSTREAM").Remove();
            if(vertex)code.Element("SHADERPROGRAMCODEBLOCK")!.AddAfterSelf(Streams.Select(s=>new XElement("CGSTREAM",new XAttribute("cgStreamName",s.Semantic),new XAttribute("cgStreamDataType","streamCount"),new XAttribute("cgStreamRenderType",s.Type))));
            code.SetAttributeValue("streamCount",vertex?Streams.Length:0);
            ReconcileInputs(code,bytes);if(vertex)NordShaderCompiler.ValidateStreams(code,bytes);
            pack.Descendants("SHADERPROGRAM").First().Parent!.Add(program);
        }
        foreach(var pass in group.Elements("SHADERGROUPPASS"))
        {
            string mask=((string)pass.Attribute("passConfigMask")!).Trim();
            pass.SetAttributeValue("vertexProgram","#"+Name+".vp");
            pass.SetAttributeValue("fragmentProgram","#"+Name+(mask=="00 00 00 04"?".normal.fp":mask=="00 00 00 02"?".depth.fp":".fp"));
        }
        var shadow=new XElement(group);shadow.SetAttributeValue("id",Name+"_shadow");shadow.Elements("LAYER").Remove();shadow.Elements("SHADERGROUPPASS").Remove();
        var shadowPass=new XElement(pack.Descendants("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="pikes_foliage_shadow55").Elements("SHADERGROUPPASS").Single());
        shadowPass.SetAttributeValue("vertexProgram","#"+Name+".shadow.vp");shadowPass.SetAttributeValue("fragmentProgram","#"+Name+".depth.fp");shadow.Add(shadowPass);shadow.SetAttributeValue("passCount",1);
        foreach(var candidate in new[]{group,shadow})
        {
            foreach(var pass in candidate.Elements("SHADERGROUPPASS"))
            {
                string vs=((string)pass.Attribute("vertexProgram")!)[1..],ps=((string)pass.Attribute("fragmentProgram")!)[1..];
                NordShaderCompiler.ValidateStreams(candidate,programs[vs]);NordShaderCompiler.ValidateLinkage(programs[vs],programs[ps],ps);
            }
            source.Parent!.Add(candidate);
        }
    }
    static void ReconcileInputs(XElement code,byte[] bytes)
    {
        int U(int at)=>checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at,4)));
        int start=Enumerable.Range(0,U(28)).Select(i=>U(32+i*4)).Single(i=>Encoding.ASCII.GetString(bytes,i,4)=="RDEF")+8;
        string Text(int offset){int at=start+offset;return Encoding.ASCII.GetString(bytes,at,Array.IndexOf(bytes,(byte)0,at)-at);}
        var names=NordShaderCompiler.TextureBindings(bytes);
        for(int i=0;i<U(start);i++) {int at=start+U(start+4)+24*i;if(Text(U(at))!="$Globals")continue;for(int j=0;j<U(at+4);j++)names.Add(Text(U(start+U(at+8)+40*j)));}
        foreach(var input in code.Elements("SHADERINPUTDEFINITION").Where(d=>!names.Contains((string)d.Attribute("name")!)).ToArray())input.Remove();
        if(!names.SetEquals(code.Elements("SHADERINPUTDEFINITION").Select(d=>(string)d.Attribute("name")!)))throw new InvalidDataException("Unbound tree billboard shader input");
        code.SetAttributeValue("parameterCount",names.Count);
    }
}
