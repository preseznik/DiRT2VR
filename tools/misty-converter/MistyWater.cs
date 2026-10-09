using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

// The lake belongs to the native non-interactive water manager, whose frame
// constants/reflections are not supplied by the ordinary terrain draw lists.
internal sealed class MistyWater(Scene scene,string game)
{
    sealed class State:PssgModelWriterState {}
    readonly Mesh[] meshes=Detach(scene);
    internal static Mesh[] Detach(Scene scene)
    {
        var result=scene.Visuals.Where(m=>m.Material.Name=="lake1").ToArray();
        scene.Visuals.RemoveAll(m=>m.Material.Name=="lake1");return result;
    }
    internal void Write(string output)
    {
        if(meshes.Length==0)throw new InvalidDataException("Source lake missing");
        string donor=Files.Inside(game,"tracks/baja/baja_iron/route_0/niwater.pssg");scene.Inputs[donor]=Files.Hash(donor);
        var file=Files.Pssg(donor);var layout=ShaderInputInfo.CreateFromPssg(file).Single(s=>s.ShaderGroupId=="water.fx");
        var render=file.Elements<PssgRenderNode>().Single();
        foreach(var draw in file.Elements<PssgRenderInstance>().ToArray())draw.ParentElement!.RemoveChild(draw);
        var segmentLib=file.Elements<PssgLibrary>().Single(l=>l.Type=="SEGMENTSET");segmentLib.RemoveChildElements();
        var dataLib=file.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");
        foreach(var item in dataLib.ChildElements.Where(e=>e is not PssgTexture).ToArray())dataLib.RemoveChild(item);
        var texture=file.Elements<PssgTexture>().Single();
        using(var input=new MemoryStream(scene.Textures[meshes[0].Material.Textures["txNormal"]].Dds,false))new DdsFile(input).ToPssgElement(texture);
        texture.AutoMipMap=false;
        render.Id="misty_lake";render.Nickname="misty_lake";
        var state=new State();var bounds=new Bounds(new(float.MaxValue),new(float.MinValue));int n=0;
        foreach(var mesh in meshes)
        {
            string id="misty_water_"+n++;var writer=new RenderDataSourceWriter(id);
            for(int i=0;i<mesh.Positions.Length;i++)
            {
                var p=mesh.Positions[i];bounds=bounds.Union(new(p,p));
                writer.Positions.Add(p);writer.Normals.Add(mesh.Normals[i]);writer.Tangents.Add(new(mesh.Tangents[i],1));
                writer.Colors.Add(Vector4.One);writer.TexCoords0.Add(mesh.UV[i]);
            }
            writer.Indices.AddRange(mesh.Indices.Select(i=>(uint)i));
            var segment=new PssgSegmentSet(file,segmentLib){Id=id+"_segments",SegmentCount=1};segmentLib.AppendChild(segment);
            writer.Write(layout,segment,dataLib,state);segment.Segments.Single().Primitive="triangles";
            var draw=new PssgRenderStreamInstance(file,render){Id=id+"_draw",SourceCount=1,Indices="#"+id,StreamCount=0,Shader="#wetness"};
            render.AppendChild(draw);draw.AppendChild(new PssgRenderInstanceSource(file,draw){Source="#"+id});
        }
        foreach(var node in file.Elements<PssgNode>())
        {
            node.Transform.Transform=Matrix4x4.Identity;node.BoundingBox.BoundsMin=bounds.Min-new Vector3(.3f);node.BoundingBox.BoundsMax=bounds.Max+new Vector3(.3f);
        }
        Files.Save(file,Path.Combine(output,"niwater.pssg"));
        // The source water geometry and winding are verified independently from terrain.
        var expected=new Dictionary<GeometryCoverage.ExactFace,int>();var actual=new Dictionary<GeometryCoverage.ExactFace,int>();
        void Add(Dictionary<GeometryCoverage.ExactFace,int> set,Vector3 a,Vector3 b,Vector3 c)
        {var key=GeometryCoverage.Exact(new(a,b,c,"lake"));set[key]=set.GetValueOrDefault(key)+1;}
        foreach(var mesh in meshes)for(int i=0;i<mesh.Indices.Length;i+=3)Add(expected,mesh.Positions[mesh.Indices[i]],mesh.Positions[mesh.Indices[i+1]],mesh.Positions[mesh.Indices[i+2]]);
        foreach(var data in Files.Pssg(Path.Combine(output,"niwater.pssg")).Elements<PssgRenderDataSource>())
        {var r=new RenderDataSourceReader(data);for(int i=0;i<r.IndexCount;i+=3)Add(actual,r.GetPosition(r.GetIndex(i)),r.GetPosition(r.GetIndex(i+1)),r.GetPosition(r.GetIndex(i+2)));}
        if(expected.Count!=actual.Count||expected.Any(p=>actual.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidDataException("Lake geometry changed");
        File.Delete(Path.Combine(output,"niwater.xml"));
        Files.Xml(new(new XElement("interactiveWater",new XElement("interactiveWaterPatch",new XAttribute("index",0),new XAttribute("uri","niwater.pssg#misty_lake"),new XAttribute("type","MistyLoch_Lake")))),Path.Combine(output,"niwater.xml"));
        string waterDefs=Files.Inside(game,"tracks/waterdefs.xml");scene.Inputs[waterDefs]=Files.Hash(waterDefs);
        using var definitionsInput=File.OpenRead(waterDefs);var definitions=XDocument.Parse(new XmlFile(definitionsInput).Document.OuterXml);
        var definition=new XElement(definitions.Root!.Elements("waterDef").Single(e=>(string?)e.Attribute("type")=="Water"));
        definition.SetAttributeValue("type","MistyLoch_Lake");
        var settings=new Dictionary<string,string>{["i_water_draw_distance"]="3000",["i_water_wave_scale"]="0.07",["i_water_wave_speed_u"]="0.13",["i_water_wave_speed_v"]="0.21",["i_water_bump_scale"]="0.03",["i_water_speed_scale"]="0.3",["i_water_specular_multiplier"]="1.0",["i_water_specular_curvepower"]="400",["i_water_colour_nadir"]="0.09 0.16 0.17 1",["i_water_colour_horizon"]="0.18 0.25 0.28 1",["i_water_colour_nadir_deep"]="0.045 0.095 0.12 1",["i_water_colour_horizon_deep"]="0.18 0.25 0.28 1",["i_water_fresnel_min"]="0.2",["i_water_fresnel_max"]="0.5"};
        foreach(var (key,value) in settings)definition.SetAttributeValue(key,value);
        definitions.Root.Elements("waterDef").Where(e=>(string?)e.Attribute("type")=="MistyLoch_Lake").Remove();definitions.Root.Add(definition);
        Directory.CreateDirectory(Path.Combine(output,"support"));Files.Xml(definitions,Path.Combine(output,"support/waterdefs.xml"));
        Files.Json(Path.Combine(output,"water-audit.json"),new{Passed=true,SourceTriangles=meshes.Sum(m=>m.Indices.Length/3),PositionsWindingExact=true,NativeWaterManager=true,OriginalNormalMapRetained=true,Settings=settings,RuntimeValidated=false});
    }
}
