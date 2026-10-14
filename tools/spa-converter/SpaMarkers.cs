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

internal static class SpaMarkers
{
    static string A(XElement e, string name) => (string?)e.Attribute(name) ?? throw new InvalidDataException("Missing " + name);
    internal static bool IsMarker(string name) => name.StartsWith("core_game_brake_01_", StringComparison.Ordinal);
    internal static string NativeName(string model)
    {
        if(!IsMarker(model))throw new InvalidDataException("Not a source braking board: "+model);
        string name="spa_marker_"+model["core_game_brake_01_".Length..];
        // Native ornament registration stores 33 characters plus a terminator.
        // Longer PSSG IDs are valid, but the entity-to-ornament lookup then fails.
        if(name.Length>33)throw new InvalidDataException("Native ornament name exceeds 33 characters");
        return name;
    }
    static IEnumerable<PssgElement> Below(PssgElement e) => new[] { e }.Concat(e.ChildElements.SelectMany(Below));
    static Matrix4x4 Matrix(string value)
    {
        var f = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (f.Length != 16 || f.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid furniture transform");
        return new(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
    }
    static Matrix4x4 World(PssgNode n)
    {
        var result = n.Transform.Transform;
        for (var p = n.ParentElement; p != null; p = p.ParentElement) if (p is PssgNode node) result *= node.Transform.Transform;
        return result;
    }
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
    internal static void Write(Scene scene,string sourceRootPath,string donor,string output)
    {
        Entities.RegisterSchema(true);
        string venue=Files.Inside(sourceRootPath,"tracks/locations/europe/spa_francorchamps"),shared=Path.Combine(output,"shared");
        var inputs=scene.Inputs;
        string Input(string root,string name){string p=Files.Inside(root,name);inputs.TryAdd(p,Files.Hash(p));return p;}
        var sourceEns=XDocument.Load(Input(venue,"route_0/objects.ens"));
        var sourceTypes=XDocument.Load(Input(venue,"route_0/route_objecttypes.pssg"));
        var definitions=sourceTypes.Root!.Element("PSSGDATABASE")!.Elements().ToDictionary(e=>A(e,"id"));
        var references=sourceEns.Descendants("TEMPLATEENTITYREFERENCE").ToDictionary(e=>A(e,"id"),e=>A(e,"uri"));
        const int expectedInstances=24,expectedModels=7;
        const string prefix="spa_marker_",modelPrefix="spa_marker_";
        var placements=sourceEns.Descendants().Where(e=>e.Name.LocalName is "TEMPLATEENTITYINSTANCE" or "TEMPLATEBASICENTITYINSTANCE").Where(e=>IsMarker(A(e,"uri")[1..]) && string.IsNullOrEmpty((string?)e.Attribute("mode_layer"))).ToArray();
        if(placements.Length!=expectedInstances)throw new InvalidDataException("Unexpected source furniture count");
        var source=Files.Pssg(Input(venue,"objects.pssg"));
        var sourceRoute=Files.Pssg(Input(venue,"route_0/route_objects.pssg"));
        var textureFiles=new Dictionary<string,PssgFile>();
        var sourceRoots=source.Elements<PssgNode>().Concat(sourceRoute.Elements<PssgNode>()).Where(n=>n.Name=="ROOTNODE").ToDictionary(n=>n.Id);
        var objects=Files.Pssg(Input(donor,"objects.pssg"));var types=Files.Pssg(Input(donor,"objecttypes.pssg"));
        var ens=Files.Pssg(Path.Combine(output,"objects.ens"));
        var originalObjects=Files.Document(objects);var originalTypes=Files.Document(types);var originalEns=Files.Document(ens);
        var ornaments=XDocument.Load(Path.Combine(output,"ornaments.xml"));var oldOrnaments=File.ReadAllBytes(Path.Combine(output,"ornaments.bin"));
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
        var writerState=new WriterState{DataBlockCount=400000u,RenderStreamCount=400000u};
        var nativeGroup=objects.GetObject<PssgShaderGroup>("object_simple.fx".AsMemory());
        var materialTemplate=objects.Elements<PssgShaderInstance>().First(s=>s.GetShaderGroup().Id=="object_simple.fx");
        var shaderMap=new Dictionary<string,string>();
        var textureChecks=new Dictionary<string,byte[]>();
        var typeAdditions=new List<XElement>();var ensAdditions=new List<XElement>();var nodeAdditions=new List<XElement>();
        var geometryChecks=new Dictionary<string,RenderDataSourceWriter>();var modelRecords=new List<object>();
        Vector3 worldMin=new(float.PositiveInfinity),worldMax=new(float.NegativeInfinity);int slot=oldTotal,meshIndex=0,shapeIndex=0;
        foreach(var group in groups){
            string model=group.Key[..^4],name=NativeName(model);var sourceRoot=sourceRoots[model+" Root"];
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
                    if(!textureFiles.TryGetValue(file,out var textureFile)){textureFile=Files.Pssg(Input(venue,file));textureFiles.Add(file,textureFile);}
                    texture=textureFile.GetObject<PssgTexture>(textureRef[(split+1)..].AsMemory());
                }
                if(!shaderMap.TryGetValue(texture.Id,out string? shaderId)){
                    shaderId=prefix+"material_"+shaderMap.Count;shaderMap.Add(texture.Id,shaderId);
                    string textureId=prefix+"texture_"+(shaderMap.Count-1);
                    var copy=(PssgTexture)texture.DeepClone();copy.Id=textureId;data.AppendChild(copy);
                    var dds=texture.ToDdsFile();DdsAlpha.Prepare(dds,true);dds.ToPssgElement(copy);copy.AutoMipMap=false;
                    using(var memory=new MemoryStream()){copy.ToDdsFile().Write(memory);textureChecks[textureId]=memory.ToArray();}
                    var xml=Xml(materialTemplate);xml.SetAttributeValue("id",shaderId);
                    var defsTarget=nativeGroup.InputDefinitions.ToArray();
                    for(int j=0;j<defsTarget.Length;j++)if(defsTarget[j].Type=="texture"){
                        var input=xml.Elements("SHADERINPUT").SingleOrDefault(e=>(int?)e.Attribute("parameterID")==j);
                        if(input is null){input=new XElement("SHADERINPUT",new XAttribute("parameterID",j));xml.Add(input);}
                        input.SetAttributeValue("type","texture");input.RemoveNodes();
                        input.SetAttributeValue("texture",defsTarget[j].InputName=="TDiffuseAlphaMap"?"#"+textureId:defsTarget[j].InputName=="TOcclusionMap"?"tracksplit.pssg#nord_white":"tracksplit.pssg#nord_black");
                    }
                    xml.SetAttributeValue("parameterSavedCount",xml.Elements("SHADERINPUT").Count());
                    Merge(objects,Files.FromDocument(Document(new[]{new XElement("LIBRARY",new XAttribute("type","SHADERINSTANCE"),xml)})));
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
                    // A whole intact box must not inherit the mass of solid rubber.
                    // Native cardboard retains PLAS contacts at one tenth the density.
                    shape.SetAttributeValue("props","physparams.xml#sCardboard");
                    if(shape.Name.LocalName is not("TEMPLATESHAPEBOX" or "TEMPLATESHAPECYLINDER" or "TEMPLATESHAPESPHERE"))throw new InvalidDataException("Unsupported sign collider");
                    typeAdditions.Add(shape);shapes.Add((id,Matrix(sr.Element("TEMPLATETRANSFORM")!.Value)*World(bodyNode)));
                }
            }
            if(shapes.Count==0)throw new InvalidDataException("Missing source sign collision: "+model);
            var template=new XElement("TEMPLATEENTITY",new XAttribute("id",name+".max"));
            foreach(string state in new[]{"upright","fallen"}){
                var body=new XElement("TEMPLATERIGIDBODY",new XAttribute("id",name+"_"+state+"_rigidbody"),new XAttribute("props","physparams.xml#rbDefault"),new XAttribute("infMass",state=="upright"?1:0));
                foreach(var (id,transform) in shapes){var s=new XElement("TEMPLATERIGIDBODYSHAPE",new XAttribute("uri","#"+id),new XElement("TEMPLATETRANSFORM",Form(transform)));if(state=="upright")s.Add(new XElement("TEMPLATEBEHAVIOUR",new XAttribute("id",id+"_impact"),new XAttribute("impulse","physparams.xml#bPhantom"),new XAttribute("typeuri","#"+name+"_fallen_behaviour")));body.Add(s);}
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
                var transform=Matrix(placement.Element("TEMPLATETRANSFORM")!.Value);transform.Translation-=scene.Origin;
                // A singleton source placement can have the same name as its model.
                // Native ENS IDs share a namespace with TEMPLATEENTITYREFERENCE.
                string placementId=modelPrefix+A(placement,"id");
                if(placementId==name)placementId+="_placement";
                ensAdditions.Add(new XElement("TEMPLATEENTITYINSTANCE",new XAttribute("id",placementId),new XAttribute("uri","#"+name),new XAttribute("instanceID",slot++),new XAttribute("staticVis",0),new XElement("TEMPLATETRANSFORM",Form(transform))));
                foreach(var p in localPoints){var world=Vector3.Transform(p,transform);worldMin=Vector3.Min(worldMin,world);worldMax=Vector3.Max(worldMax,world);}
            }
            modelRecords.Add(new{Model=model,Instances=group.Count(),NativeModel=name,SourceShapes=shapes.Count,IntactRigidSignKnocksDown=true,SourceShapeDimensionsAndPlacementRetained=true,ArtAndGeometryRetained=true,SourceSkinDeformationReplacedByRigidMotion=true,NativeLightweightBoardMaterial="sCardboard"});
        }
        if(slot!=oldTotal+expectedInstances)throw new InvalidDataException("Incorrect traffic entity slots");
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
        UniqueResources(objects);
        ValidNodeNames(objects);
        Files.Save(objects,Path.Combine(shared,"objects.pssg"));Files.Save(types,Path.Combine(shared,"objecttypes.pssg"));
        using(var stream=new FileStream(Path.Combine(output,"objects.ens"),FileMode.Truncate))ens.Save(stream);
        ornaments.Save(Path.Combine(output,"ornaments.xml"));File.WriteAllBytes(Path.Combine(output,"ornaments.bin"),packedOrnaments);
        var saved=Files.Pssg(Path.Combine(shared,"objects.pssg"));
        UniqueResources(saved);
        ValidNodeNames(saved);
        foreach(var(id,payload) in textureChecks)
        {using var memory=new MemoryStream();saved.GetObject<PssgTexture>(id.AsMemory()).ToDdsFile().Write(memory);if(!payload.AsSpan().SequenceEqual(memory.ToArray()))throw new InvalidDataException("Marker texture mip readback differs");}
        foreach(var(id,w) in geometryChecks){var r=new RenderDataSourceReader(saved.GetObject<PssgRenderDataSource>(id.AsMemory()));if(r.VertexCount!=w.Positions.Count||r.IndexCount!=w.Indices.Count)throw new InvalidDataException("Sign geometry readback count");for(uint v=0;v<r.VertexCount;v++)if(Vector3.Distance(r.GetPosition(v),w.Positions[(int)v])>1e-6f||Vector3.Distance(r.GetNormal(v),w.Normals[(int)v])>.001f||Vector2.Distance(r.GetTexCoord(v,0),w.TexCoords0[(int)v])>.001f)throw new InvalidDataException("Sign vertex payload mismatch");for(int i=0;i<r.IndexCount;i++)if(r.GetIndex(i)!=w.Indices[i])throw new InvalidDataException("Sign topology mismatch");}
        // Binary round-trip validates the entity graph independently of preparation.
        foreach(var(name,old) in new[]{("objects.pssg",originalObjects),("objecttypes.pssg",originalTypes),("objects.ens",originalEns)}){
            var savedDoc=Files.Document(Files.Pssg(Path.Combine(name=="objects.ens"?output:shared,name)));
            foreach(var lib in old.Descendants("LIBRARY"))foreach(var element in lib.Elements()){
                string? id=(string?)element.Attribute("id");var candidates=savedDoc.Descendants("LIBRARY").Where(l=>(string?)l.Attribute("type")== (string?)lib.Attribute("type")).Elements().Where(e=>e.Name==element.Name&&(string?)e.Attribute("id")==id);
                if(!candidates.Any(e=>XNode.DeepEquals(e,element)))throw new InvalidDataException("Unrelated native object changed: "+name+"/"+id);
            }
        }
        if(!oldOrnaments.AsSpan(oldEnd).SequenceEqual(packedOrnaments.AsSpan(oldEnd+shift,oldOrnaments.Length-oldEnd)))throw new InvalidDataException("Existing ornament payload changed");
        foreach(var(p,h) in inputs)if(Files.Hash(p)!=h)throw new InvalidDataException("Protected source changed");
        Verify(shared,output,placements,references,scene.Origin);
        Files.Json(Path.Combine(output,"markers.json"),new{Passed=true,SourceInstances=expectedInstances,SourceModels=expectedModels,OriginalEntitySlots=oldTotal,EntitySlots=slot,Models=modelRecords,SourcePoseAndGeometryRetained=true,TextureMipsReadBack=textureChecks.Count,OpaqueCoverageMatchesAcceptedBoards=true,NativeImpactThreshold="bPhantom",NativeBoardMaterial="sCardboard",SourceFragmentsReplacedByWholeBoardRigidKnockdown=true,OriginalObjectsTypesEntitiesAndOrnamentPayloadsPreserved=true,GameplayLaunched=false,PlayerImpactAcceptancePending=true});
        Console.WriteLine($"Prepared {placements.Length} knock-away braking markers with {shapeIndex} original collider shapes.");
    }
    static void UniqueResources(PssgFile file)
    {
        var duplicates=file.Elements<PssgElement>()
            .SelectMany(e=>e.Attributes.Where(a=>a.Name=="id").Select(a=>a.DisplayValue))
            .GroupBy(id=>id,StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>g.Key).ToArray();
        if(duplicates.Length!=0)
            throw new InvalidDataException($"Duplicate object resource IDs ({duplicates.Length}): {string.Join(", ",duplicates.Take(3))}");
    }

    static void ValidNodeNames(PssgFile file)
    {
        var roots=file.Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE" && n.Id.StartsWith("spa_marker_",StringComparison.Ordinal)).ToArray();
        if(roots.Length!=7)throw new InvalidDataException("Marker roots missing");
        foreach(var root in roots)foreach(var node in Below(root).OfType<PssgNode>().Skip(1))
            if(!node.Attributes.Any(a=>a.Name=="nickname" && a.DisplayValue==(node.Name=="LODVISIBLERENDERNODE"?"lod":node.Id)))throw new InvalidDataException("Native marker nickname missing");
    }
    static void Verify(string shared,string output,XElement[] placements,Dictionary<string,string> references,Vector3 origin)
    {
        var objects=Files.Pssg(Path.Combine(shared,"objects.pssg"));
        var types=Files.Document(Files.Pssg(Path.Combine(shared,"objecttypes.pssg")));
        var ens=Files.Document(Files.Pssg(Path.Combine(output,"objects.ens")));
        var defs=types.Descendants().Where(e=>e.Attribute("id")!=null).GroupBy(e=>A(e,"id")).ToDictionary(g=>g.Key,g=>g.First());
        var markers=types.Descendants("TEMPLATEENTITY").Where(e=>A(e,"id").StartsWith("spa_marker_",StringComparison.Ordinal)).ToArray();
        if(markers.Length!=7)throw new InvalidDataException("Marker model inventory");
        foreach(var marker in markers){
            string name=A(marker,"id")[..^4];
            var initial=marker.Elements("TEMPLATEBEHAVIOUR").Single();
            if(!A(initial,"id").StartsWith("initial",StringComparison.Ordinal)||A(initial,"typeuri")!="#"+name+"_upright_behaviour")throw new InvalidDataException("Initial marker state");
            foreach(string state in new[]{"upright","fallen"}){
                var sub=marker.Elements("TEMPLATESUBENTITY").Single(e=>A(e,"id")==name+"_"+state+"_subent");
                var body=sub.Element("TEMPLATERIGIDBODY")!;
                if((int)body.Attribute("infMass")! != (state=="upright"?1:0))throw new InvalidDataException("Marker mass state");
                var shape=body.Elements("TEMPLATERIGIDBODYSHAPE").Single();
                if(defs[A(shape,"uri")[1..]].Name.LocalName!="TEMPLATESHAPEBOX")throw new InvalidDataException("Original board box missing");
                var impact=shape.Element("TEMPLATEBEHAVIOUR");
                if(state=="upright"?(impact==null||A(impact,"impulse")!="physparams.xml#bPhantom"||A(impact,"typeuri")!="#"+name+"_fallen_behaviour"):impact!=null)throw new InvalidDataException("Marker impact transition");
                var behaviour=defs[name+"_"+state+"_behaviour"];
                var ops=behaviour.Elements("TEMPLATECOMPONENTCHANGE").ToArray();
                if(ops.Length!=2||!ops.Any(e=>A(e,"operation")=="add"&&A(e,"componenturi")=="#"+A(sub,"id"))||!ops.Any(e=>A(e,"operation")=="delete"&&A(e,"componenturi")!="#"+A(sub,"id")))throw new InvalidDataException("Marker component transitions");
                var transformable=sub.Element("TEMPLATETRANSFORMABLE")!;var render=defs[A(transformable,"uri")[1..]];
                var root=objects.GetObject<PssgNode>(A(render,"uri").Split('#').Last().AsMemory());
                if(!Below(root).OfType<PssgNode>().Any(n=>n.Id==A(transformable,"sid")))throw new InvalidDataException("Marker rigid render node missing");
            }
        }
        var instances=ens.Descendants("TEMPLATEENTITYINSTANCE").ToArray();
        if(instances.Select(e=>A(e,"instanceID")).Distinct().Count()!=instances.Length)throw new InvalidDataException("Duplicate entity slots");
        foreach(var p in placements){
            string model=references[A(p,"uri")[1..]].Split('#').Last()[..^4],name=NativeName(model);
            var reference=ens.Descendants("TEMPLATEENTITYREFERENCE").Single(e=>A(e,"id")==name);
            if((int)reference.Attribute("allocAlt")! !=5)throw new InvalidDataException("Marker alternate state allocation");
            string id="spa_marker_"+A(p,"id");if(id==name)id+="_placement";
            var saved=instances.Single(e=>A(e,"id")==id);var expected=Matrix(p.Element("TEMPLATETRANSFORM")!.Value);expected.Translation-=origin;
            if(Matrix(saved.Element("TEMPLATETRANSFORM")!.Value)!=expected||A(saved,"uri")!="#"+name)throw new InvalidDataException("Marker placement differs from source");
        }
    }
}
