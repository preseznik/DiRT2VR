using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static partial class PikesRoadObjects
{
    sealed class WriterState : PssgModelWriterState { }
    static XElement Xml(PssgElement e) { var root=new XElement("ROOT"); e.WriteXml(root); return root.Elements().Single(); }
    static string Form(Vector3 p) => string.Join(' ',new[]{p.X,p.Y,p.Z}.Select(f=>f.ToString("R",CultureInfo.InvariantCulture)));
    static string Form(Matrix4x4 m) => string.Join(' ',new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44}.Select(f=>f.ToString("R",CultureInfo.InvariantCulture)));
    static XDocument Document(IEnumerable<XElement> values) => new(new XElement("PSSGFILE",new XElement("PSSGDATABASE",values)));
    static void Merge(PssgFile target,PssgFile fragment)
    {
        foreach(var lib in fragment.Elements<PssgLibrary>()){
            var dest=target.Elements<PssgLibrary>().SingleOrDefault(l=>l.Type==lib.Type);
            if(dest is null)target.RootElement.AppendChild(lib.DeepClone());
            else foreach(var e in lib.ChildElements)dest.AppendChild(e.DeepClone());
        }
    }
    internal static void TrafficSigns(string rally,string game,string output,bool hayBales,PikesSourceProfile profile,string candidate)
    {
        Files.NewOutput(output,rally,game); Entities.RegisterSchema(true);
        string venue=profile.PathIn(rally),track=Path.Combine(candidate,"shared"),routePath=candidate;
        var inputs=new Dictionary<string,string>();
        string Input(string root,string name){string p=Files.Inside(root,name);inputs.TryAdd(p,Files.Hash(p));return p;}
        var sourceEns=XDocument.Load(Input(venue,"route_0/objects.ens"));
        var sourceTypes=XDocument.Load(Input(venue,"route_0/route_objecttypes.pssg"));
        var definitions=sourceTypes.Root!.Element("PSSGDATABASE")!.Elements().ToDictionary(e=>A(e,"id"));
        var references=sourceEns.Descendants("TEMPLATEENTITYREFERENCE").ToDictionary(e=>A(e,"id"),e=>A(e,"uri"));
        int expectedInstances=hayBales?166:138,expectedModels=hayBales?1:21;
        string prefix=hayBales?"pikes44_bale_":"pikes40_sign_",modelPrefix=hayBales?"pikes44_":"pikes40_";
        var placements=sourceEns.Descendants().Where(e=>e.Name.LocalName is "TEMPLATEENTITYINSTANCE" or "TEMPLATEBASICENTITYINSTANCE").Where(e=>hayBales?A(e,"uri")=="#pke_haybale_a":Sign(A(e,"uri")[1..])).ToArray();
        if(profile.IsGravel){expectedInstances=placements.Length;expectedModels=placements.Select(e=>references[A(e,"uri")[1..]]).Distinct().Count();}
        if(placements.Length!=expectedInstances)throw new InvalidDataException("Unexpected source furniture count");
        var source=PikesSponsorBarrier.Read(Input(venue,"objects.pssg"));
        var sourceRoute=PikesSponsorBarrier.Read(Input(venue,"route_0/route_objects.pssg"));
        var textureFiles=new Dictionary<string,PssgFile>();
        var sourceRoots=source.Elements<PssgNode>().Concat(sourceRoute.Elements<PssgNode>()).Where(n=>n.Name=="ROOTNODE").ToDictionary(n=>n.Id);
        var objects=Files.Pssg(Input(track,"objects.pssg"));var types=Files.Pssg(Input(track,"objecttypes.pssg"));
        var ens=Files.Pssg(Input(routePath,"objects.ens"));var route=Files.Pssg(Input(routePath,"routesplit.pssg"));
        var originalObjects=Files.Document(objects);var originalTypes=Files.Document(types);var originalEns=Files.Document(ens);
        var ornaments=XDocument.Load(Input(routePath,"ornaments.xml"));var oldOrnaments=File.ReadAllBytes(Input(routePath,"ornaments.bin"));
        int OldInt(int p)=>BinaryPrimitives.ReadInt32LittleEndian(oldOrnaments.AsSpan(p,4));
        int oldCount=OldInt(40),oldTotal=OldInt(28);if(OldInt(36)!=44||ornaments.Root!.Elements("instanceref").Count()!=oldCount)throw new InvalidDataException("Ornament header mismatch");
        var groups=placements.GroupBy(e=>references[A(e,"uri")[1..]].Split('#').Last()).OrderBy(g=>g.Key,StringComparer.Ordinal).ToArray();
        if(groups.Length!=expectedModels)throw new InvalidDataException("Traffic model set mismatch");
        int shift=groups.Length*56,oldEnd=44+oldCount*56;
        using var bytes=new MemoryStream();bytes.Write(oldOrnaments.AsSpan(0,oldEnd));bytes.Write(new byte[shift]);bytes.Write(oldOrnaments.AsSpan(oldEnd));
        void Put(int p,int v){long end=bytes.Position;bytes.Position=p;Span<byte> b=stackalloc byte[4];BinaryPrimitives.WriteInt32LittleEndian(b,v);bytes.Write(b);bytes.Position=end;}
        void Float(int p,float v)=>Put(p,BitConverter.SingleToInt32Bits(v));
        void Bounds(int row,Vector3 min,Vector3 max){Float(row+4,min.X);Float(row+8,min.Y);Float(row+12,min.Z);Float(row+16,max.X);Float(row+20,max.Y);Float(row+24,max.Z);}
        for(int i=0;i<oldCount;i++){int r=44+i*56;foreach(int field in new[]{0,44,52})if(OldInt(r+field)>0)Put(r+field,OldInt(r+field)+shift);}
        Put(40,oldCount+groups.Length);Put(28,oldTotal+placements.Length);
        var layout=ShaderInputInfo.CreateFromPssg(objects).Single(l=>l.ShaderGroupId=="object_simple.fx");
        var objectMaterials=objects.Elements<PssgLibrary>().Single(l=>l.Type=="SHADERINSTANCE");
        var data=objects.Elements<PssgLibrary>().Single(l=>l.Type=="RENDERINTERFACEBOUND");var sets=objects.Elements<PssgLibrary>().Single(l=>l.Type=="SEGMENTSET");
        var writerState=new WriterState{DataBlockCount=hayBales?300000u:200000u,RenderStreamCount=hayBales?300000u:200000u};
        var nativeGroup=objects.GetObject<PssgShaderGroup>("object_simple.fx".AsMemory());
        var mapping=JsonDocument.Parse(File.ReadAllBytes(Input(candidate,"materials.json"))).RootElement.EnumerateArray().ToArray();
        var shaderMap=new Dictionary<string,string>();var removedMaterials=new HashSet<string>();var expectedStaticTriangles=new Dictionary<string,int>();
        var typeAdditions=new List<XElement>();var ensAdditions=new List<XElement>();var nodeAdditions=new List<XElement>();
        var geometryChecks=new Dictionary<string,RenderDataSourceWriter>();var modelRecords=new List<object>();
        Vector3 worldMin=new(float.PositiveInfinity),worldMax=new(float.NegativeInfinity);int slot=oldTotal,meshIndex=0,shapeIndex=0;
        foreach(var group in groups){
            string model=group.Key[..^4],name=modelPrefix+model;var sourceRoot=sourceRoots[model+" Root"];
            var lods=sourceRoot.ChildElements.OfType<PssgNode>().Where(n=>n.Name=="LODVISIBLERENDERNODE").ToArray();
            if(lods.Length!=1)throw new InvalidDataException("Unexpected traffic static LODs: "+model);
            var meshes=new List<(string Source,string Shader)>();var localPoints=new List<Vector3>();
            foreach(var draw in lods.Single().ChildElements.OfType<PssgRenderStreamInstance>()){
                var material=draw.GetShaderInstance();var defs=material.GetShaderGroup().InputDefinitions.ToArray();
                string textureRef=material.Inputs.Single(i=>i.Type=="texture"&&defs[i.ParameterId].InputName=="TDiffuseAlphaMap").Texture;
                PssgTexture texture;
                if(textureRef.StartsWith('#'))texture=sourceRoot.File.GetObject<PssgTexture>(textureRef[1..].AsMemory());
                else {
                    int split=textureRef.IndexOf('#');if(split<1)throw new InvalidDataException("Invalid traffic texture reference");
                    string file=textureRef[..split];
                    file=file switch {"objectstextures.pssg"=>"patchup_ot.pssg","route_objectstextures.pssg"=>"route_0/route_patchup_ot.pssg",_=>throw new InvalidDataException("Unexpected sign texture library: "+file)};
                    if(!textureFiles.TryGetValue(file,out var textureFile)){textureFile=PikesSponsorBarrier.Read(Input(venue,file));textureFiles.Add(file,textureFile);}
                    texture=textureFile.GetObject<PssgTexture>(textureRef[(split+1)..].AsMemory());
                }
                if(profile.IsGravel){const string sponsor="texture_sponsor_patch.pssg";if(!textureFiles.TryGetValue(sponsor,out var patch)){patch=PikesSponsorBarrier.Read(Input(venue,sponsor));textureFiles.Add(sponsor,patch);}texture=patch.TryGetObject<PssgTexture>(texture.Id.AsMemory())??texture;}
                using var dds=new MemoryStream();texture.ToDdsFile().Write(dds);string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dds.ToArray()));
                var matches=mapping.Select((m,i)=>(m,i)).Where(p=>p.m.GetProperty("Diffuse").ValueKind==JsonValueKind.String&&p.m.GetProperty("Diffuse").GetString()=="pikes_"+hash).ToArray();
                if(matches.Length!=1)throw new InvalidDataException("Accepted sign material not unique: "+material.Id+"/"+hash+"/"+matches.Length);
                string oldShader="nord_material_"+matches[0].i;
                var accepted=route.GetObject<PssgShaderInstance>(oldShader.AsMemory());var acceptedDefs=accepted.GetShaderGroup().InputDefinitions.ToArray();
                string acceptedTexture=accepted.Inputs.Single(i=>acceptedDefs[i.ParameterId].InputName=="TDiffuseAlphaMap").Texture;
                removedMaterials.Add("#"+oldShader);
                if(!shaderMap.TryGetValue(oldShader,out string? shaderId)){
                    shaderId=prefix+"material_"+shaderMap.Count;shaderMap.Add(oldShader,shaderId);
                    if(accepted.GetShaderGroup().Id!="object_simple.fx" || !acceptedDefs.Select(d=>d.InputName).SequenceEqual(nativeGroup.InputDefinitions.Select(d=>d.InputName)))throw new InvalidDataException("Sign material layout mismatch");
                    var shader=(PssgShaderInstance)accepted.DeepClone();shader.Id=shaderId;objectMaterials.AppendChild(shader);
                    var names=shader.GetShaderGroup().InputDefinitions.ToArray();shader.Inputs.Single(i=>names[i.ParameterId].InputName=="TDiffuseAlphaMap").Texture=acceptedTexture;
                }
                var reader=new RenderDataSourceReader(draw.GetRenderDataSource());string id=prefix+"rds_"+meshIndex++;
                var writer=new RenderDataSourceWriter(id);var transform=World(lods.Single());if(!Matrix4x4.Invert(transform,out var inverse))throw new InvalidDataException("Singular source model");
                for(uint v=0;v<reader.VertexCount;v++){
                    var p=Vector3.Transform(reader.GetPosition(v),transform);writer.Positions.Add(p);localPoints.Add(p);
                    writer.Normals.Add(Vector3.Normalize(Vector3.TransformNormal(reader.GetNormal(v),Matrix4x4.Transpose(inverse))));
                    writer.Tangents.Add(new(1,0,0,1));writer.Colors.Add(Vector4.One);writer.TexCoords0.Add(reader.GetTexCoord(v,0));writer.TexCoords1.Add(reader.GetTexCoord(v,0));
                }
                for(int i=0;i<reader.IndexCount;i++)writer.Indices.Add(reader.GetIndex(i));
                var set=new PssgSegmentSet(objects,sets){Id=id+"_segments",SegmentCount=1};sets.AppendChild(set);writer.Write(layout,set,data,writerState);set.Segments.Single().Primitive="triangles";
                // The writer gives the index buffer the mesh ID too; native loading
                // requires unique IDs even when the resources have different types.
                objects.Elements<PssgRenderDataSource>().Single(r=>r.Id==id).IndexSource!.Id=id+"_indices";
                geometryChecks.Add(id,writer);meshes.Add((id,shaderId));
                expectedStaticTriangles["#"+oldShader]=expectedStaticTriangles.GetValueOrDefault("#"+oldShader)+(int)reader.IndexCount/3*group.Count();
            }
            Vector3 min=localPoints.Aggregate(Vector3.Min),max=localPoints.Aggregate(Vector3.Max);
            // The native ornament loader compares child nicknames to "lod" without a null check.
            XElement Node(string tag,string id,IEnumerable<XElement> children)=>new(tag,new XAttribute("id",id),new XAttribute("stopTraversal",0),tag=="ROOTNODE"?null:new XAttribute("nickname",tag=="LODVISIBLERENDERNODE"?"lod":id),new XElement("TRANSFORM",Form(Matrix4x4.Identity)),new XElement("BOUNDINGBOX",Form(min)+" "+Form(max)),children);
            IEnumerable<XElement> Draws(string state)=>meshes.Select((m,i)=>new XElement("RENDERSTREAMINSTANCE",new XAttribute("id",name+state+"_draw_"+i),new XAttribute("indices","#"+m.Source),new XAttribute("shader","#"+m.Shader),new XAttribute("sourceCount",1),new XAttribute("streamCount",0),new XElement("RENDERINSTANCESOURCE",new XAttribute("source","#"+m.Source))));
            var rootChildren=new List<XElement>{Node("LODVISIBLERENDERNODE",name+"_lod",Draws("lod").Append(new XElement("LODRENDERINSTANCES")))};
            foreach(string state in new[]{"upright","fallen"})rootChildren.Add(Node("NODE",name+"_"+state,new[]{Node("NODE",name+"_"+state+"_body",new[]{Node("RENDERNODE",name+"_"+state+"_draws",Draws(state))})}));
            nodeAdditions.Add(Node("ROOTNODE",name+" Root",rootChildren));
            var sourceEntity=definitions[group.Key];string initial=A(sourceEntity.Elements("TEMPLATEBEHAVIOUR").Single(),"typeuri")[1..];
            var active=definitions[initial].Elements("TEMPLATECOMPONENTCHANGE").Where(e=>A(e,"operation")=="add").Select(e=>A(e,"componenturi")[1..]).ToHashSet();
            var shapes=new List<(string Id,Matrix4x4 Transform)>();
            foreach(var sub in sourceEntity.Elements("TEMPLATESUBENTITY").Where(s=>active.Contains(A(s,"id")))){
                var body=sub.Element("TEMPLATERIGIDBODY");if(body is null)continue;
                string renderUri=A(definitions[A(sub.Element("TEMPLATETRANSFORMABLE")!,"uri")[1..]],"uri");
                var frame=sourceRoot.File.GetObject<PssgNode>(renderUri.Split('#').Last().AsMemory());
                // Each shape is expressed in the original body's render frame.
                string sid=A(sub.Element("TEMPLATETRANSFORMABLE")!,"sid");
                var bodyNode=Below(frame).OfType<PssgNode>().Single(n=>n.Id==sid);
                foreach(var sr in body.Elements("TEMPLATERIGIDBODYSHAPE")){
                    var shape=new XElement(definitions[A(sr,"uri")[1..]]);string id=prefix+"shape_"+shapeIndex++;shape.SetAttributeValue("id",id);
                    if(hayBales)shape.SetAttributeValue("props","physparams.xml#sHaybail");
                    if(shape.Name.LocalName is not("TEMPLATESHAPEBOX" or "TEMPLATESHAPECYLINDER" or "TEMPLATESHAPESPHERE"))throw new InvalidDataException("Unsupported sign collider");
                    typeAdditions.Add(shape);shapes.Add((id,Matrix(sr.Element("TEMPLATETRANSFORM")!.Value)*World(bodyNode)));
                }
            }
            if(shapes.Count==0)throw new InvalidDataException("Missing source sign collision: "+model);
            var template=new XElement("TEMPLATEENTITY",new XAttribute("id",name+".max"));
            foreach(string state in new[]{"upright","fallen"}){
                var body=new XElement("TEMPLATERIGIDBODY",new XAttribute("id",name+"_"+state+"_rigidbody"),new XAttribute("props","physparams.xml#rbDefault"),new XAttribute("infMass",state=="upright"?1:0));
                foreach(var (id,transform) in shapes){var s=new XElement("TEMPLATERIGIDBODYSHAPE",new XAttribute("uri","#"+id),new XElement("TEMPLATETRANSFORM",Form(transform)));if(state=="upright")s.Add(new XElement("TEMPLATEBEHAVIOUR",new XAttribute("id",id+"_impact"),new XAttribute("impulse","physparams.xml#bLight"),new XAttribute("typeuri","#"+name+"_fallen_behaviour")));body.Add(s);}
                template.Add(new XElement("TEMPLATESUBENTITY",new XAttribute("id",name+"_"+state+"_subent"),body,new XElement("TEMPLATETRANSFORMABLE",new XAttribute("uri","#"+name+"_"+state+"_renderable"),new XAttribute("sid",name+"_"+state+"_body"))));
                typeAdditions.Add(new XElement("TEMPLATERENDERABLE",new XAttribute("id",name+"_"+state+"_renderable"),new XAttribute("uri","objects.pssg#"+name+"_"+state)));
                typeAdditions.Add(new XElement("TEMPLATEBEHAVIOURTYPE",new XAttribute("id",name+"_"+state+"_behaviour"),new[]{"upright","fallen"}.Select(s=>new XElement("TEMPLATECOMPONENTCHANGE",new XAttribute("componenturi","#"+name+"_"+s+"_subent"),new XAttribute("operation",s==state?"add":"delete")))));
            }
            // The native reader tests the first seven ID characters for "initial".
            template.Add(new XElement("TEMPLATEBEHAVIOUR",new XAttribute("id","initial!"+name),new XAttribute("impulse","physparams.xml#bDefault"),new XAttribute("typeuri","#"+name+"_upright_behaviour")));typeAdditions.Add(template);
            ensAdditions.Add(new XElement("TEMPLATEENTITYREFERENCE",new XAttribute("id",name),new XAttribute("uri","objecttypes.pssg#"+name+".max"),new XAttribute("allocAlt",5)));
            int row=44+(oldCount+modelRecords.Count)*56;Put(row,checked((int)bytes.Length));bytes.Position=bytes.Length;bytes.Write(Encoding.ASCII.GetBytes(name+"\0"));while(bytes.Length%4!=0)bytes.WriteByte(0);
            Bounds(row,min,max);Put(row+36,group.Count());Put(row+40,slot);Put(row+44,checked((int)bytes.Length));Put(row+52,-1);
            ornaments.Root.Add(new XElement("instanceref",new XAttribute("filename",name),new XAttribute("prebaked_shadows",0),new XAttribute("sponsor",0),new XAttribute("bounds_min",Form(min)),new XAttribute("bounds_max",Form(max)),new XAttribute("max_instances",group.Count()),new XAttribute("offset",slot)));
            foreach(var placement in group){
                var transform=Matrix(placement.Element("TEMPLATETRANSFORM")!.Value);transform.Translation-=Origin;
                // A singleton source placement can have the same name as its model.
                // Native ENS IDs share a namespace with TEMPLATEENTITYREFERENCE.
                string placementId=modelPrefix+A(placement,"id");
                if(placementId==name)placementId+="_placement";
                ensAdditions.Add(new XElement("TEMPLATEENTITYINSTANCE",new XAttribute("id",placementId),new XAttribute("uri","#"+name),new XAttribute("instanceID",slot++),new XAttribute("staticVis",0),new XElement("TEMPLATETRANSFORM",Form(transform))));
                foreach(var p in localPoints){var world=Vector3.Transform(p,transform);worldMin=Vector3.Min(worldMin,world);worldMax=Vector3.Max(worldMax,world);}
            }
            modelRecords.Add(new{Model=model,Instances=group.Count(),NativeModel=name,SourceShapes=shapes.Count,IntactRigidSignKnocksDown=true,SourceShapeDimensionsAndPlacementRetained=true,ArtAndGeometryRetained=true,SourceSkinDeformationReplacedByRigidMotion=true});
        }
        if(slot!=oldTotal+expectedInstances)throw new InvalidDataException("Incorrect traffic entity slots");
        // All traffic materials must be dedicated to exactly this source geometry.
        var removal=new List<object>();var removedSources=new HashSet<string>();var actualStatic=new Dictionary<string,int>();
        foreach(var draw in route.Elements<PssgRenderStreamInstance>().Where(d=>removedMaterials.Contains(d.Shader)).ToArray()){
            var sourceId=draw.GetRenderDataSource().Id;
            if(removedSources.Add(sourceId))actualStatic[draw.Shader]=actualStatic.GetValueOrDefault(draw.Shader)+(int)new RenderDataSourceReader(draw.GetRenderDataSource()).IndexCount/3;
            removal.Add(new{draw.Id,draw.Shader,Source=sourceId});draw.ParentElement!.RemoveChild(draw);
        }
        Files.Json(Path.Combine(output,"static-removal-check.json"),new{Expected=expectedStaticTriangles,Actual=actualStatic,Draws=removal});
        if(expectedStaticTriangles.Any(p=>actualStatic.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidDataException("Traffic material is shared, missing, or duplicated; retain installed scene and inspect static-removal-check.json");
        var depthIds=removedSources.Select(s=>s.Replace("nord_","nord_depth_",StringComparison.Ordinal)).ToHashSet();
        foreach(var draw in route.Elements<PssgRenderStreamInstance>().Where(d=>depthIds.Contains(d.GetRenderDataSource().Id)).ToArray())draw.ParentElement!.RemoveChild(draw);
        // Preserve every existing ornament payload; only pointer offsets change as rows are appended.
        for(int i=0;i<3;i++){float lo=BitConverter.Int32BitsToSingle(OldInt(4+i*4)),hi=BitConverter.Int32BitsToSingle(OldInt(16+i*4));Float(4+i*4,Math.Min(lo,i==0?worldMin.X:i==1?worldMin.Y:worldMin.Z));Float(16+i*4,Math.Max(hi,i==0?worldMax.X:i==1?worldMax.Y:worldMax.Z));}
        var packedOrnaments=bytes.ToArray();
        float S(int p)=>BinaryPrimitives.ReadSingleLittleEndian(packedOrnaments.AsSpan(p,4));
        ornaments.Root.SetAttributeValue("total_instances",slot);ornaments.Root.SetAttributeValue("bounds_min",Form(new Vector3(S(4),S(8),S(12)))+" 1");ornaments.Root.SetAttributeValue("bounds_max",Form(new Vector3(S(16),S(20),S(24)))+" 1");
        var td=Document(typeAdditions);Entities.ValidateText(td);Entities.Convert(td,true);Merge(types,Files.FromDocument(td));
        var ed=Document(ensAdditions);Entities.ValidateText(ed);Entities.Convert(ed,true);Merge(ens,Files.FromDocument(ed));
        var entityIds=ens.Elements<PssgElement>().SelectMany(e=>e.Attributes.Where(a=>a.Name=="id").Select(a=>a.DisplayValue)).ToArray();
        if(entityIds.Distinct(StringComparer.Ordinal).Count()!=entityIds.Length)throw new InvalidDataException("Entity placement/reference identities collide");
        var nd=Document(new[]{new XElement("LIBRARY",new XAttribute("type","NODE"),nodeAdditions)});Merge(objects,Files.FromDocument(nd));
        UniqueSignResources(objects);
        ValidSignNodeNames(objects,profile.IsGravel?(hayBales?null:expectedModels):21);
        Files.Save(objects,Path.Combine(output,"objects.pssg"));Files.Save(types,Path.Combine(output,"objecttypes.pssg"));Files.Save(ens,Path.Combine(output,"objects.ens"));Files.Save(route,Path.Combine(output,"routesplit.pssg"));
        ornaments.Save(Path.Combine(output,"ornaments.xml"));File.WriteAllBytes(Path.Combine(output,"ornaments.bin"),packedOrnaments);
        var saved=Files.Pssg(Path.Combine(output,"objects.pssg"));
        UniqueSignResources(saved);
        ValidSignNodeNames(saved,profile.IsGravel?(hayBales?null:expectedModels):21);
        foreach(var(id,w) in geometryChecks){var r=new RenderDataSourceReader(saved.GetObject<PssgRenderDataSource>(id.AsMemory()));if(r.VertexCount!=w.Positions.Count||r.IndexCount!=w.Indices.Count)throw new InvalidDataException("Sign geometry readback count");for(uint v=0;v<r.VertexCount;v++)if(Vector3.Distance(r.GetPosition(v),w.Positions[(int)v])>1e-6f||Vector3.Distance(r.GetNormal(v),w.Normals[(int)v])>.001f||Vector2.Distance(r.GetTexCoord(v,0),w.TexCoords0[(int)v])>.001f)throw new InvalidDataException("Sign vertex payload mismatch");for(int i=0;i<r.IndexCount;i++)if(r.GetIndex(i)!=w.Indices[i])throw new InvalidDataException("Sign topology mismatch");}
        // Binary round-trip validates the entity graph independently of preparation.
        foreach(var(name,old) in new[]{("objects.pssg",originalObjects),("objecttypes.pssg",originalTypes),("objects.ens",originalEns)}){
            var savedDoc=Files.Document(Files.Pssg(Path.Combine(output,name)));
            foreach(var lib in old.Descendants("LIBRARY"))foreach(var element in lib.Elements()){
                string? id=(string?)element.Attribute("id");var candidates=savedDoc.Descendants("LIBRARY").Where(l=>(string?)l.Attribute("type")== (string?)lib.Attribute("type")).Elements().Where(e=>e.Name==element.Name&&(string?)e.Attribute("id")==id);
                if(!candidates.Any(e=>XNode.DeepEquals(e,element)))throw new InvalidDataException("Unrelated native object changed: "+name+"/"+id);
            }
        }
        if(!oldOrnaments.AsSpan(oldEnd).SequenceEqual(packedOrnaments.AsSpan(oldEnd+shift,oldOrnaments.Length-oldEnd)))throw new InvalidDataException("Existing ornament payload changed");
        foreach(var(p,h) in inputs)if(Files.Hash(p)!=h)throw new InvalidDataException("Protected source changed");
        Files.Json(Path.Combine(output,"traffic-signs.json"),new{Passed=true,SourceInstances=expectedInstances,SourceModels=expectedModels,HayBales=hayBales,OriginalEntitySlots=oldTotal,EntitySlots=slot,SourceInputs=inputs,Models=modelRecords,SourceShaderTexturesReused=true,StaticTrianglesRemoved=actualStatic.Values.Sum(),StaticDepthCopiesRemoved=true,SourcePoseAndGeometryRetained=true,NativeImpactThreshold="bLight",SourceSkinDeformationReplacedByWholeSignRigidKnockdown=true,OriginalObjectsTypesEntitiesAndOrnamentPayloadsPreserved=true,GameplayLaunched=false,PlayerImpactAcceptancePending=true});
        Console.WriteLine($"Prepared {placements.Length} knock-away {(hayBales ? "hay bales" : "signs")} with {shapeIndex} source collider shapes; {actualStatic.Values.Sum()} baked triangles replaced.");
    }
}
