using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;
using EgoEngineLibrary.Formats.Pssg;

internal static class MizuFoliage
{
    const string AoGroup="object_simple_ao.fx", Atlas="mizu_bush_occlusion";
    readonly record struct VertexKey(string Material,Vector3 Position,Vector3 Normal,Vector2 Uv);
    static bool Bush(string name)=>name.StartsWith("bush_leaf_01",StringComparison.Ordinal);
    static Vector2 HalfUv(Vector2 uv)=>new((float)(Half)uv.X,(float)(Half)uv.Y);
    static Matrix4x4 World(PssgNode node)
    {
        var world=node.Transform.Transform;
        for(var parent=node.ParentElement;parent is not null;parent=parent.ParentElement)
            if(parent is PssgNode n)world*=n.Transform.Transform;
        return world;
    }
    internal static void Write(string grid2,string candidate,Vector3 origin,Dictionary<string,string> inputs)
    {
        string venue=Files.Inside(grid2,"tracks/locations/p2p/okutama");
        string Input(string relative){string path=Files.Inside(venue,relative);Files.NoLinks(path);inputs[path]=Files.Hash(path);return path;}
        using var lightingStream=File.OpenRead(Input("lighting_day.xml"));
        var lighting=XDocument.Parse(new XmlFile(lightingStream).Document.OuterXml);
        float Param(string name)=>float.Parse(lighting.Descendants("param").Single(p=>p.Attribute(name) is not null).Attribute(name)!.Value,CultureInfo.InvariantCulture);
        float centre=Param("treeDarkenCenter"),bottom=Param("treeDarkenBottom"),darkest=Param("treeDarkest"),normalise=Param("treeNormaliseVC"),shadowing=Param("treeShadowing");
        var shadow=new SourceShadow(Input("route_0/shadow_map_day.clm"));
        var source=Files.Pssg(Input("trees.pssg"));
        var roots=source.Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE"&&n.Id.Contains("bush",StringComparison.OrdinalIgnoreCase)).ToDictionary(n=>n.Id);
        var modelBounds=new Dictionary<string,Vector3>();
        var values=new Dictionary<VertexKey,byte>();int placements=0;
        void Add(string name,Matrix4x4 placement)
        {
            if(!roots.TryGetValue(name+" Root",out var root))return;
            placements++;float sun=shadow.Sample(placement.Translation);
            if(!modelBounds.TryGetValue(root.Id,out var extent))
            {
                var positions=new List<Vector3>();
                foreach(var node in source.Elements<PssgNode>().Where(n=>n.Name=="RENDERNODE"&&!n.Id.EndsWith("_xs",StringComparison.OrdinalIgnoreCase)&&!n.Id.EndsWith("_x2",StringComparison.OrdinalIgnoreCase)&&!n.Id.EndsWith("_physics",StringComparison.OrdinalIgnoreCase)))
                {
                    var ancestor=node.ParentElement;while(ancestor is not null&&!ReferenceEquals(ancestor,root))ancestor=ancestor.ParentElement;if(ancestor is null)continue;
                    foreach(var draw in node.ChildElements.OfType<PssgRenderStreamInstance>().Where(d=>Bush(d.GetShaderInstance().Id)))
                    {
                        var reader=new RenderDataSourceReader(draw.GetRenderDataSource());var world=World(node);
                        positions.AddRange(Enumerable.Range(0,checked((int)reader.IndexCount)).Select(reader.GetIndex).Distinct().Select(i=>Vector3.Transform(reader.GetPosition(i),world)));
                    }
                }
                if(positions.Count==0)return;
                extent=positions.Aggregate(Vector3.Max)-positions.Aggregate(Vector3.Min);modelBounds[root.Id]=extent;
            }
            float radius=MathF.Max(extent.X,extent.Z)/2,height=extent.Y;
            if(radius<=0||height<=0)throw new InvalidDataException("Invalid bush bounds");
            foreach(var node in source.Elements<PssgNode>().Where(n=>n.Name=="RENDERNODE"&&!n.Id.EndsWith("_xs",StringComparison.OrdinalIgnoreCase)&&!n.Id.EndsWith("_x2",StringComparison.OrdinalIgnoreCase)&&!n.Id.EndsWith("_physics",StringComparison.OrdinalIgnoreCase)))
            {
                var ancestor=node.ParentElement;while(ancestor is not null&&!ReferenceEquals(ancestor,root))ancestor=ancestor.ParentElement;
                if(ancestor is null)continue;
                var localWorld=World(node);var world=localWorld*placement;
                if(!Matrix4x4.Invert(world,out var inverse))throw new InvalidDataException("Singular bush placement");
                var normals=Matrix4x4.Transpose(inverse);
                foreach(var draw in node.ChildElements.OfType<PssgRenderStreamInstance>())
                {
                    string material=draw.GetShaderInstance().Id;if(!Bush(material))continue;
                    var reader=new RenderDataSourceReader(draw.GetRenderDataSource());
                    foreach(uint i in Enumerable.Range(0,checked((int)reader.IndexCount)).Select(reader.GetIndex).Distinct())
                    {
                        var p=reader.GetPosition(i);var volumePosition=Vector3.Transform(p,localWorld)/new Vector3(radius,height,radius);volumePosition.X*=1.5f;volumePosition.Z*=1.5f;
                        float volume=MathF.Max(MathF.Min(Math.Clamp(1+volumePosition.LengthSquared()-centre,0,1),Math.Clamp(1+volumePosition.Y*volumePosition.Y-bottom,0,1)),darkest);
                        var colour=reader.GetColor(i);var rgb=Vector3.Max(new(colour.X,colour.Y,colour.Z),new Vector3(1f/512));
                        rgb=Vector3.Lerp(rgb,rgb*(3/(rgb.X+rgb.Y+rgb.Z)),normalise);
                        float luma=Math.Clamp(Vector3.Dot(rgb,new(.2126f,.7152f,.0722f)),0,1);
                        // Approximate spatial ambient response with the source scalar sun map.
                        // Keep a source-defined ambient floor; this is not GRID 2's coloured GI.
                        float shade=volume*luma*(1-shadowing+shadowing*sun);
                        byte value=checked((byte)Math.Clamp((int)MathF.Round(shade*255),0,255));
                        var normal=Vector3.TransformNormal(reader.GetNormal(i),normals);normal=normal.LengthSquared()>1e-8f?Vector3.Normalize(normal):Vector3.UnitY;
                        var key=new VertexKey(material,Vector3.Transform(p,world)-origin,normal,HalfUv(reader.GetTexCoord(i,0)));
                        if(values.TryGetValue(key,out byte previous)&&Math.Abs(previous-value)>1)throw new InvalidDataException("Conflicting source bush shading");
                        values[key]=value;
                    }
                }
            }
        }
        var bytes=File.ReadAllBytes(Input("route_0/trees.bin"));
        int I(int at)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at,4));
        float S(int at)=>BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at,4));
        if(I(0)!=0||I(8)!=1)throw new InvalidDataException("Unsupported tree placement layout");
        int list=I(4),refs=I(list+36),count=I(list+40),instances=I(list+44),n=I(list+48);
        var names=new Dictionary<int,string>();
        for(int r=0;r<count;r++){int p=refs+r*40,at=I(p);names.Add(I(p+4),Encoding.ASCII.GetString(bytes,at,Array.IndexOf(bytes,(byte)0,at)-at));}
        for(int i=0;i<n;i++)
        {
            int p=instances+i*76;float[] f=new float[16];f[15]=1;int[] fields=[0,1,2,4,5,6,8,9,10,12,13,14];
            for(int j=0;j<12;j++)f[fields[j]]=S(p+8+j*4);
            Add(names[I(p)],Matrix(f));
        }
        var entities=XDocument.Load(Input("route_0/objects.ens"));
        var references=entities.Descendants("TEMPLATEENTITYREFERENCE").ToDictionary(e=>(string)e.Attribute("id")!,e=>(string)e.Attribute("uri")!);
        foreach(var entity in entities.Descendants().Where(e=>e.Name.LocalName is "TEMPLATEENTITYINSTANCE" or "TEMPLATEBASICENTITYINSTANCE"))
        {
            string name=((string)entity.Attribute("uri")!).TrimStart('#');
            if(references.TryGetValue(name,out string? reference)){name=reference[(reference.IndexOf('#')+1)..];if(name.EndsWith(".max",StringComparison.Ordinal))name=name[..^4];}
            if(!roots.ContainsKey(name+" Root"))continue;
            var f=entity.Element("TEMPLATETRANSFORM")!.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
            if(f.Length!=16)throw new InvalidDataException("Invalid entity matrix");Add(name,Matrix(f));
        }
        Apply(candidate,values,placements,new{centre,bottom,darkest,normalise,shadowing});
    }
    static Matrix4x4 Matrix(float[] f)=>new(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]);
    static void Apply(string candidate,Dictionary<VertexKey,byte> source,int placements,object parameters)
    {
        var route=Files.Pssg(Files.Inside(candidate,"routesplit.pssg"));var textures=Files.Pssg(Files.Inside(candidate,"shared/tracksplit.pssg"));
        var groups=route.Elements<PssgLibrary>().Single(l=>l.Type=="SHADERGROUP");
        if(!route.Elements<PssgShaderGroup>().Any(g=>g.Id==AoGroup))
        {
            var xml=new XElement("root");route.GetObject<PssgShaderGroup>("object_simple.fx".AsMemory()).WriteXml(xml);xml.Elements().Single().SetAttributeValue("id",AoGroup);
            groups.AppendChild(PssgElement.ReadXml(xml.Elements().Single(),route,groups));
        }
        string materialPath=Files.Inside(candidate,"materials.json");var materials=JsonNode.Parse(File.ReadAllText(materialPath))!.AsArray();var selected=new Dictionary<string,string>();
        for(int i=0;i<materials.Count;i++)if(Bush(materials[i]!["Name"]!.GetValue<string>()))
        {
            var shader=route.GetObject<PssgShaderInstance>(("nord_material_"+i).AsMemory());
            var definitions=shader.GetShaderGroup().InputDefinitions.ToArray();shader.Inputs.Single(p=>definitions[p.ParameterId].InputName=="TOcclusionMap").Texture="tracksplit.pssg#"+Atlas;shader.ShaderGroup="#"+AoGroup;
            selected["#"+shader.Id]=materials[i]!["Name"]!.GetValue<string>();materials[i]!["Target"]=AoGroup;
            materials[i]!["Approximation"]="Native diffuse AO from normalized source vertex colour, bush volume controls and scalar source sun shadow; coloured ambient GI remains approximate";
        }
        if(selected.Count!=6)throw new InvalidDataException("Expected six bush materials");
        var patches=new Dictionary<(string Block,uint Index),byte>();var originals=new Dictionary<string,byte[]>();int draws=0;
        foreach(var draw in route.Elements<PssgRenderStreamInstance>().Where(d=>selected.ContainsKey(d.Shader)).DistinctBy(d=>d.GetRenderDataSource().Id))
        {
            draws++;var rds=draw.GetRenderDataSource();var reader=new RenderDataSourceReader(rds);var block=route.GetObject<PssgDataBlock>(rds.Elements<PssgRenderStream>().First().DataBlock.AsMemory(1));
            var streams=block.Streams.ToArray();var uv=streams.Where(s=>s.RenderType=="ST").Skip(1).Single();
            if(uv.DataType!="half2"||uv.Stride!=32||uv.Offset!=16)throw new InvalidDataException("Unexpected bush UV1 layout");
            if(!originals.ContainsKey(block.Id))originals[block.Id]=(byte[])block.Data.Value.Clone();
            foreach(uint i in Enumerable.Range(0,checked((int)reader.IndexCount)).Select(reader.GetIndex).Distinct())
            {
                var key=new VertexKey(selected[draw.Shader],reader.GetPosition(i),reader.GetNormal(i),reader.GetTexCoord(i,0));
                if(!source.TryGetValue(key,out byte value))throw new InvalidDataException("Missing original bush vertex: "+key);
                var address=(block.Id,i);if(patches.TryGetValue(address,out byte old)&&old!=value)throw new InvalidDataException("Conflicting shared bush UV1");patches[address]=value;
                int at=checked((int)(uv.Offset+i*uv.Stride));BinaryPrimitives.WriteHalfBigEndian(block.Data.Value.AsSpan(at,2),(Half)((value+.5f)/256));BinaryPrimitives.WriteHalfBigEndian(block.Data.Value.AsSpan(at+2,2),(Half).5f);
            }
        }
        if(patches.Count==0)throw new InvalidDataException("No bush vertices patched");
        foreach(var (id,original) in originals)
        {
            var current=route.GetObject<PssgDataBlock>(id.AsMemory()).Data.Value;
            for(int at=0;at<original.Length;at++)if(original[at]!=current[at]&&(at%32 is <16 or >19||!patches.ContainsKey((id,(uint)(at/32)))))throw new InvalidDataException("Foliage edit changed a protected vertex byte");
        }
        var library=textures.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");foreach(var old in textures.Elements<PssgTexture>().Where(t=>t.Id==Atlas).ToArray())library.RemoveChild(old);
        var white=textures.GetObject<PssgTexture>("nord_white".AsMemory());var textureXml=new XElement("root");white.WriteXml(textureXml);
        var texture=(PssgTexture)PssgElement.ReadXml(textureXml.Elements().Single(),textures,library);texture.Id=Atlas;texture.AutoMipMap=false;
        // Native object_simple_ao samples G for diffuse occlusion and R for
        // optional baked direct shadow. Keep R white; bake our scalar into G.
        var dds=white.ToDdsFile();dds.header.width=256;dds.header.height=1;dds.header.pitchOrLinearSize=1024;dds.bdata=Enumerable.Range(0,256).SelectMany(i=>new byte[]{255,(byte)i,255,255}).ToArray();dds.ToPssgElement(texture);library.AppendChild(texture);
        var indices=route.Elements<PssgRenderStreamInstance>().Select(d=>d.GetRenderDataSource()).DistinctBy(d=>d.Id).ToDictionary(d=>d.Id,d=>FilesBytes(d.IndexSource!.Data.Value));
        string assembly=Path.Combine(candidate,"foliage-assembly");
        if(Directory.Exists(assembly))throw new InvalidDataException("Existing foliage staging preserved");Directory.CreateDirectory(assembly);
        Files.Save(route,Path.Combine(assembly,"routesplit.pssg"));Files.Save(textures,Path.Combine(assembly,"tracksplit.pssg"));
        var saved=Files.Pssg(Path.Combine(assembly,"routesplit.pssg"));
        foreach(var (id,original) in originals)if(!route.GetObject<PssgDataBlock>(id.AsMemory()).Data.Value.AsSpan().SequenceEqual(saved.GetObject<PssgDataBlock>(id.AsMemory()).Data.Value))throw new InvalidDataException("Bush buffer roundtrip failed");
        foreach(var rds in saved.Elements<PssgRenderStreamInstance>().Select(d=>d.GetRenderDataSource()).DistinctBy(d=>d.Id))if(indices[rds.Id]!=FilesBytes(rds.IndexSource!.Data.Value))throw new InvalidDataException("Bush edit changed indices");
        File.Move(Path.Combine(assembly,"routesplit.pssg"),Path.Combine(candidate,"routesplit.pssg"),true);File.Move(Path.Combine(assembly,"tracksplit.pssg"),Path.Combine(candidate,"shared/tracksplit.pssg"),true);Directory.Delete(assembly);
        File.WriteAllText(materialPath,materials.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        string adapterPath=Files.Inside(candidate,"source-adapter.json");var adapter=JsonNode.Parse(File.ReadAllText(adapterPath))!;adapter["NativeBushOcclusion"]=true;File.WriteAllText(adapterPath,adapter.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        Files.Json(Path.Combine(candidate,"foliage-materials.json"),new{Materials=selected.Values,SourceBushPlacements=placements,SourceVertices=source.Count,DrawSources=draws,PatchedVertices=patches.Count,
            MinOcclusion=patches.Values.Min()/255f,MaxOcclusion=patches.Values.Max()/255f,SourceParameters=parameters,OnlyBushUv1BytesChanged=true,AllIndicesByteIdentical=true,PositionsNormalsDiffuseUvsAndAlphaPreserved=true,
            HdrAndTrackLightingUnchanged=true,Approximation="Scalar source vertex/volume shading and a 2D source sun-shadow sample approximate ambient response; original coloured ambient SH, foliage translucency and wind are not reconstructed",RuntimeValidated=false});
    }
    static string FilesBytes(byte[] data)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
    sealed class SourceShadow
    {
        readonly byte[] bytes;readonly float ox,oz,sx,sz;readonly int resolution,radix,coverage;
        internal SourceShadow(string path)
        {
            bytes=File.ReadAllBytes(path);uint prefix=BinaryPrimitives.ReadUInt32LittleEndian(bytes);if(prefix!=0x14010002)throw new InvalidDataException("Unsupported source scalar shadow CLM");
            float F(int at)=>BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at,4));int I(int at)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at,4));
            ox=F(4);oz=F(8);sx=F(12);sz=F(16);resolution=I(20);radix=I(24);if(resolution<=0||radix!=8)throw new InvalidDataException("Unsupported source shadow dimensions");coverage=radix;while(coverage<resolution)coverage=checked(coverage*radix);
        }
        internal float Sample(Vector3 world)
        {
            int x=(int)MathF.Floor((world.X-ox)*sx),z=(int)MathF.Floor((world.Z-oz)*sz);if(x<0||z<0||x>=resolution||z>=resolution)throw new InvalidDataException("Bush outside source shadow map");
            int node=28,size=coverage;
            while(size>1)
            {
                size/=radix;int cell=z/size*radix+x/size;x%=size;z%=size;
                int value=unchecked((sbyte)bytes[checked(node+4+cell)]);if(value>=0)return value/127f;
                node=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(node,4))+(-value-1)*(4+radix*radix));
            }
            throw new InvalidDataException("Invalid source shadow leaf");
        }
    }
}