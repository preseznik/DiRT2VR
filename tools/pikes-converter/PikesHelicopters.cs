using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class PikesHelicopters
{
    internal sealed record Flight(string Name,Vector4[] Positions,(float Time,Quaternion Rotation)[] Rotations)
    {
        internal Vector3 Initial => new(Positions[0].Y,Positions[0].Z,Positions[0].W);
        internal Vector3 Min => Positions.Select(p=>new Vector3(p.Y,p.Z,p.W)).Aggregate(Vector3.Min)-new Vector3(12);
        internal Vector3 Max => Positions.Select(p=>new Vector3(p.Y,p.Z,p.W)).Aggregate(Vector3.Max)+new Vector3(12);
    }
    internal static Flight[] Read(string rally,PikesSourceProfile profile,Vector3 origin)
    {
        var file=Files.Document(PikesSponsorBarrier.Read(profile.PathIn(rally,"route_0/ornament_path_anims.pssg")));
        var positions=new Dictionary<string,Vector4[]>();var rotations=new Dictionary<string,(float,Quaternion)[]>();
        foreach(var packet in file.Descendants().Where(n=>n.Name.LocalName is "NeAnimPacketData_L4" or "NeAnimPacketData_SC4"))
        {
            byte[] data=Convert.FromHexString(string.Concat(packet.Value.Where(c=>!char.IsWhiteSpace(c))));int offset=8;
            string name=Text(),channel=Text();int count=Int();_ = Int();_ = Int();
            if(count is <1 or >1024)throw new InvalidDataException("Invalid source helicopter key count");
            if(channel=="translation")
            {
                var keys=new Vector4[count];
                for(int i=0;i<count;i++){float t=Float();var p=new Vector3(Float(),Float(),Float())-origin;_ = Float();keys[i]=new(t,p.X,p.Y,p.Z);}
                positions.Add(name,keys);
            }
            else if(channel=="rotation_quaternion")
            {
                var keys=new (float,Quaternion)[count];
                for(int i=0;i<count;i++)
                {
                    float t=Float();var q=Quaternion.Normalize(new(Half(),Half(),Half(),Half()));
                    if(i>0&&Quaternion.Dot(keys[i-1].Item2,q)<0)q=new(-q.X,-q.Y,-q.Z,-q.W);
                    keys[i]=(t,q);
                }
                rotations.Add(name,keys);
            }
            else throw new InvalidDataException("Unsupported helicopter channel");
            if(offset!=data.Length)throw new InvalidDataException("Unexpected helicopter packet length");
            int Int(){int value=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset,4));offset+=4;return value;}
            float Float()=>BitConverter.Int32BitsToSingle(Int());
            float Half(){float value=(float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset,2)));offset+=2;return value;}
            string Text(){int length=Int();string value=Encoding.UTF8.GetString(data,offset,length);offset+=length;return value;}
        }
        var result=positions.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new Flight(p.Key,p.Value,rotations[p.Key])).ToArray();
        if(result.Length!=4)throw new InvalidDataException("Expected four source helicopter paths");
        return result;
    }
    internal static byte[] Program(string rally)
    {
        // The shared shader library retains the modern course flight program.
        // Historic gravel has no helicopter placements or flight animation file.
        var flights=Read(rally,PikesSourceProfile.Modern,new(1556.6499f,54.7f,-4592.365f));
        var source=new StringBuilder();
        for(int i=0;i<flights.Length;i++)
        {
            var f=flights[i];int positions=f.Positions.Length,rotations=f.Rotations.Length;string duration=F(f.Positions[^1].X);
            source.Append($"static const float4 positions{i}[{positions}]={{"+string.Join(",",f.Positions.Select(V))+"};\n");
            source.Append($"static const float times{i}[{rotations}]={{"+string.Join(",",f.Rotations.Select(r=>F(r.Time)))+"};\n");
            source.Append($"static const float4 rotations{i}[{rotations}]={{"+string.Join(",",f.Rotations.Select(r=>V(new(r.Rotation.X,r.Rotation.Y,r.Rotation.Z,r.Rotation.W))))+"};\n");
            source.Append($$"""
                void Flight{{i}}(inout float3 p,inout float3 n){
                  float t=frac(timer/{{duration}})*{{duration}};
                  float3 position=positions{{i}}[0].yzw;
                  [loop] for(int k=1;k<{{positions}};k++){
                    if(t>=positions{{i}}[k-1].x && t<=positions{{i}}[k].x)
                      position=lerp(positions{{i}}[k-1].yzw,positions{{i}}[k].yzw,saturate((t-positions{{i}}[k-1].x)/(positions{{i}}[k].x-positions{{i}}[k-1].x)));
                  }
                  float4 q=rotations{{i}}[0];
                  [loop] for(int k=1;k<{{rotations}};k++){
                    if(t>=times{{i}}[k-1] && t<=times{{i}}[k])
                      q=normalize(lerp(rotations{{i}}[k-1],rotations{{i}}[k],saturate((t-times{{i}}[k-1])/(times{{i}}[k]-times{{i}}[k-1]))));
                  }
                  float4 inverseInitial=rotations{{i}}[0]*float4(-1,-1,-1,1);
                  p=Rotate(q,Rotate(inverseInitial,p-positions{{i}}[0].yzw))+position;
                  n=Rotate(q,Rotate(inverseInitial,n));
                }
                """);
        }
        return PikesShaderCompiler.Compile(PikesResources.Text("Helicopter.hlsl").Replace("// SOURCE_FLIGHTS",source.ToString(),StringComparison.Ordinal),"vs_5_0");
    }
    static string F(float value)=>value.ToString("G9",CultureInfo.InvariantCulture);
    static string V(Vector4 v)=>$"float4({F(v.X)},{F(v.Y)},{F(v.Z)},{F(v.W)})";

    internal static Flight[] Apply(Scene scene,string rally,PikesSourceProfile profile,string shaderPath)
    {
        if(profile.IsGravel)
        {
            if(scene.Visuals.Any(m=>m.Material.Name=="cal_helicopter_rotars"))throw new InvalidDataException("Unexpected helicopter in the historic source");
            return [];
        }
        var flights=Read(rally,profile,scene.Origin);
        var groups=Files.Document(Files.Pssg(shaderPath)).Descendants("SHADERGROUP").ToDictionary(g=>(string)g.Attribute("id")!);
        var sourceTexture=PikesSponsorBarrier.Read(profile.PathIn(rally,"patchup_ot.pssg")).GetObject<PssgTexture>("cal_helicopter_02_anim_d.tga".AsMemory());
        using var dds=new MemoryStream();sourceTexture.ToDdsFile().Write(dds);byte[] pixels=dds.ToArray();
        var texture=new Texture(pixels,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
        scene.Textures.Add("pikes_helicopter_rotor",texture);
        var originals=scene.Visuals.Where(m=>m.Material.Name is "Material #47" or "cal_helicopter_rotars" or "glass!3").ToArray();
        var pieces=new List<(Mesh Mesh,int Index)>();
        foreach(var original in originals)
        {
            var assignment=original.Positions.Select(p=>Enumerable.Range(0,flights.Length).MinBy(i=>Vector3.DistanceSquared(p,flights[i].Initial))).ToArray();
            if(original.Positions.Where((p,i)=>Vector3.Distance(p,flights[assignment[i]].Initial)>15).Any())
                throw new InvalidDataException("Helicopter geometry is not on its source path");
            var triangles=Enumerable.Range(0,original.Indices.Length/3).Select(i=>original.Indices.AsSpan(i*3,3).ToArray()).ToArray();
            if(triangles.Any(t=>t.Select(i=>assignment[i]).Distinct().Count()!=1))throw new InvalidDataException("Helicopter triangle crosses source instances");
            foreach(int index in assignment.Distinct().Order())
            {
                var sourceIndices=triangles.Where(t=>assignment[t[0]]==index).SelectMany(t=>t).ToArray();
                var used=sourceIndices.Distinct().ToArray();var remap=used.Select((v,i)=>(v,i)).ToDictionary(p=>p.v,p=>checked((ushort)p.i));
                T[] Select<T>(T[] data)=>used.Select(i=>data[i]).ToArray();
                var piece=original with{Name=original.Name+"_flight"+index,Positions=Select(original.Positions),Normals=Select(original.Normals),UV=Select(original.UV),Tangents=Select(original.Tangents),Indices=sourceIndices.Select(i=>remap[i]).ToArray(),
                    Colors=original.Colors is {} colours?Select(colours):null,Binormals=original.Binormals is {} binormals?Select(binormals):null,TexCoords=original.TexCoords?.Select(Select).ToArray()};
                pieces.Add((piece,index));
            }
        }
        if(pieces.Count!=12||pieces.Select(p=>p.Index).Distinct().Count()!=4)throw new InvalidDataException("Incomplete source helicopter geometry");
        foreach(var original in originals)scene.Visuals.Remove(original);
        foreach(var (mesh,index) in pieces)
        {
            bool rotor=mesh.Material.Name=="cal_helicopter_rotars";string groupName=rotor?"object_scrolling":"object_rotating";
            var group=new XElement(groups[groupName]);group.SetAttributeValue("id",groupName+".fx");
            group.Elements().Where(e=>e.Name!="SHADERINPUTDEFINITION").Remove();group.SetAttributeValue("parameterStreamCount",0);group.SetAttributeValue("passCount",0);group.SetAttributeValue("parameterSavedCount",0);
            string id=mesh.Material.Id+"_flight"+index;
            var instance=new XElement("SHADERINSTANCE",new XAttribute("id",id),new XAttribute("shaderGroup","#"+groupName+".fx"),new XAttribute("parameterCount",group.Elements().Count()),new XAttribute("parameterSavedCount",group.Elements().Count()),new XAttribute("renderSortPriority",rotor?100:0));
            var textures=new Dictionary<string,string>{["txDiffuse"]=rotor?"pikes_helicopter_rotor":mesh.Material.Textures["txDiffuse"]};int parameter=0;
            foreach(var definition in group.Elements("SHADERINPUTDEFINITION"))
            {
                string name=(string)definition.Attribute("name")!,type=(string)definition.Attribute("type")!;
                var input=new XElement("SHADERINPUT",new XAttribute("parameterID",parameter++),new XAttribute("type",type));
                // Matrix, lighting and timer constants are supplied by the native renderer.
                if(name is not ("FlightIndex" or "RotorEnabled" or "preBakedShadows" or "TDiffuseAlphaMap" or "TSpecularMap" or "TOcclusionMap"))continue;
                if(type=="texture")
                {
                    textures[name]=name switch{"TDiffuseAlphaMap"=>textures["txDiffuse"],"TSpecularMap"=>"nord_black","TOcclusionMap"=>"nord_white",_=>throw new InvalidDataException("Unexpected helicopter texture")};
                    input.SetAttributeValue("texture",name);
                }
                else {input.SetAttributeValue("format","float");input.Value=name switch{"FlightIndex"=>index.ToString(CultureInfo.InvariantCulture),"RotorEnabled"=>rotor?"1":"0","preBakedShadows"=>"1",_=>throw new InvalidDataException("Unexpected helicopter constant: "+name)};}
                instance.Add(input);
            }
            instance.SetAttributeValue("parameterSavedCount",instance.Elements().Count());
            var layout=new ShaderInputInfo(groupName+".fx",[new ShaderBlockInputInfo([new("Vertex","float3",0,32),new("ST","float2",12,32),new("Normal","float3",20,32)])]);
            scene.Visuals.Add(mesh with{Material=mesh.Material with{Id=id,Textures=textures,Native=new(group,instance,layout,false)}});
        }
        return flights;
    }
    internal static void DepthAndBounds(EgoEngineLibrary.Graphics.Pssg.PssgFile route,Flight[] flights,Bounds[] bounds)
    {
        foreach(var node in route.Elements<PssgRenderNode>().Where(n=>n.Id.StartsWith("HIGH_",StringComparison.Ordinal)||n.Id.StartsWith("LOW_",StringComparison.Ordinal)).ToArray())
        foreach(var draw in node.RenderInstances.Where(d=>d.GetShaderInstance().ShaderGroup is "#object_rotating.fx" or "#object_scrolling.fx").ToArray())
        {
            var shader=draw.GetShaderInstance();var definitions=shader.GetShaderGroup().InputDefinitions.ToArray();
            var value=shader.Inputs.Single(i=>definitions[i.ParameterId].InputName=="FlightIndex").Value;
            int index=(int)BinaryPrimitives.ReadSingleBigEndian(value);var flight=flights[index];
            var batch=node.ParentElement!.ChildElements.OfType<PssgRenderNode>().Single(n=>n.Id==node.Id.Insert(node.Id.IndexOf('_'),"BATCH"));
            var copy=(PssgRenderStreamInstance)draw.DeepClone();copy.Id+="_flight_depth";batch.AppendChild(copy);
            var parent=(PssgNode)node;
            while(true)
            {
                parent.BoundingBox.BoundsMin=Vector3.Min(parent.BoundingBox.BoundsMin,flight.Min);parent.BoundingBox.BoundsMax=Vector3.Max(parent.BoundingBox.BoundsMax,flight.Max);
                if(parent.ParentElement is not PssgNode next)break;parent=next;
            }
            int tile=int.Parse(node.Id[(node.Id.IndexOf('_')+1)..],CultureInfo.InvariantCulture);
            bounds[tile]=bounds[tile].Union(new(flight.Min,flight.Max));
        }
    }
}
