using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class PikesGravelBuild
{
    internal static void Run(string rally,string game,string profileName,string condition,string output,Action<int,string>? progress=null,bool performanceAssets=false)
    {
        void Phase(int percent,string message){Console.WriteLine(message);progress?.Invoke(percent,message);}
        var profile=profileName=="tarmac"?PikesSourceProfile.Modern:PikesSourceProfile.Gravel(condition);
        Files.NewOutput(output,rally,game);
        string donor=Files.Inside(game,"tracks/london/battersea");
        var guide=PikesScene.ReadSectorRoute(rally,output,0,true,profile);
        Phase(0,"Reconstructing grass and cloth placements");
        var grass=SourceGrass.ReadBaked(rally,output,guide.Origin,guide.Gates,profile:profile);
        var netting=SourceNetting.Read(rally,0,guide.Origin,Path.Combine(output,"netting"),profile);
        PikesGravelObstacles.Add(rally,profile,guide,output);
        Phase(5,"Reading approved shader contracts and "+condition+" source lighting");
        var materials=new PikesGravelMaterials(rally,profile);
        var scene=PikesScene.Read(rally,game,output,800,fullCourse:true,groundRadius:PikesGravelContact.GroundRadius,obstacleRadius:8,roadRadius:PikesGravelContact.GroundRadius,profile:profile,gravelMaterials:materials,performanceAssets:performanceAssets);
        Merge(grass);Merge(netting);scene.Collision.AddRange(guide.Collision);foreach(var pair in guide.Inputs)scene.Inputs.TryAdd(pair.Key,pair.Value);
        if(!profile.IsGravel)PikesSourceSeams.Apply(scene,output);
        PikesGravelContact.AddTrees(rally,game,scene,output,profile);
        Phase(35,"Packing source collision");
        var builder=new QuadTreeMeshDataBuilder(VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
        foreach(var face in scene.Collision)builder.Add(face);
        var ground=TrackGround.Create(builder.Build()).Save();int chunks=ground.Entries.Count-1;
        Files.Json(Path.Combine(output,"collision-budget.json"),new{Chunks=chunks,RuntimeCapacity=4096,GroundRadius=PikesGravelContact.GroundRadius,RoadRadius=PikesGravelContact.GroundRadius,Passed=chunks<=4096});
        if(chunks>4096)throw new InvalidDataException("Pikes Peak collision exceeds the approved runtime's capacity: "+chunks);
        using(var stream=File.Create(Path.Combine(output,"track.jpk")))ground.Write(stream);
        Phase(45,"Reconstructing directional canopy lighting");
        PikesCanopies.Apply(scene,rally,profile,output);
        if(performanceAssets)PikesTreeBillboards.Materials(scene);
        if(!profile.IsGravel)PikesStartPaint.Apply(scene,output);
        string layout=profile.IsGravel?condition switch{"day"=>"gravel-day","day_alt"=>"gravel-alt",_=>"gravel-wet"}:"tarmac";
        PikesShaderRecipe.Write(game,rally,layout,Path.Combine(output,"shaderpack.pssg"),performanceAssets:performanceAssets);
        PikesShaderRecipe.Write(game,rally,layout,Path.Combine(output,"postprocess.pssg"),true);
        var flights=PikesHelicopters.Apply(scene,rally,profile,Path.Combine(output,"shaderpack.pssg"));
        if(performanceAssets)PikesSceneryTextures.Apply(scene,output);
        Phase(50,performanceAssets?"Writing nearby scenery and distant tree billboards":"Writing full-detail scenery");
        var (_,bounds)=Visuals.Build(scene,donor,output,independentTiles:true,splitDetailLevels:performanceAssets);
        SurfaceFiltering.ApplyToNewBuild(output);
        var route=Files.Pssg(Path.Combine(output,"routesplit.pssg"));
        ShaderFormats.Validate(route,true);AddCanopyShadows(route);PikesHelicopters.DepthAndBounds(route,flights,bounds);ReplacePssg(route,Path.Combine(output,"routesplit.pssg"));
        Visibility.WriteDiagnostic(Path.Combine(donor,"route_1/track.vis"),output,bounds);
        Phase(70,"Writing point-to-point route and native session assets");
        PikesTrackBuild.Route(scene,donor,output);
        PikesSourceAI.Write(scene,rally,output,0,profile);
        DonorScenery.Write(scene,donor,output);Lighting.Write(scene,donor,output);
        Files.Json(Path.Combine(output,"segment.json"),new{Schema=1,SourceLayout=profile.Venue+"/route_0",Condition=condition,LengthMetres=scene.Length,TranslationOnly=new[]{scene.Origin.X,scene.Origin.Y,scene.Origin.Z},FullCourse=true,RuntimeValidated=false});
        Complete(rally,game,profile,output,scene,chunks,progress,performanceAssets);
        void Merge(Scene detail){scene.Visuals.AddRange(detail.Visuals);foreach(var pair in detail.Textures)scene.Textures.TryAdd(pair.Key,pair.Value);foreach(var pair in detail.Inputs)scene.Inputs.TryAdd(pair.Key,pair.Value);}
    }
    internal static void Complete(string rally,string game,PikesSourceProfile profile,string output,Scene scene,int chunks,Action<int,string>? progress=null,bool performanceAssets=false)
    {
        string condition=profile.Condition;string donor=Files.Inside(game,"tracks/london/battersea");
        string work=output+"-work";
        string crowdSource=Path.Combine(work,"crowd-source"),crowdFixed=Path.Combine(work,"crowd-native");
        progress?.Invoke(75,"Converting crowds and lighting");
        DetailProbe.Prepare(rally,game,output,crowdSource,profile);
        PikesCrowdFix.Prepare(crowdSource,game,crowdFixed,profile.IsGravel?null:3274);
        foreach(string name in new[]{"crowd_standing2.bin","organism_track_dataset.xml"})File.Copy(Path.Combine(crowdFixed,name),Path.Combine(output,name),true);
        PikesGravelLighting.Write(rally,game,profile,scene,output);
        if(performanceAssets)
        {
            string path=Path.Combine(output,"route_overrides.xml");var overrides=PikesSourceProfile.ReadXml(path);
            overrides.Root!.Element("default")!.Attribute("track_lod_dist")!.Value="250.0";
            ReplaceXml(overrides,path);
        }
        foreach(var file in Directory.EnumerateFiles(donor))CopyMissing(file,Path.Combine(output,"shared",Path.GetFileName(file)));
        foreach(var file in Directory.EnumerateFiles(Path.Combine(donor,"route_1")))
            if(!DonorScenery.OmittedRouteFiles.Contains(Path.GetFileName(file)))CopyMissing(file,Path.Combine(output,Path.GetFileName(file)));
        PikesPresentation.Write(rally,profile,output);
        progress?.Invoke(80,"Checking scenery coverage");
        PikesTrackBuild.Verify(scene,output);
        progress?.Invoke(88,"Preparing Rally and Grid race starts");
        PikesRaceQueues.Write(scene,game,output,output,false,profile.IsGravel);
        PikesRaceQueues.Write(scene,game,output,output,true,profile.IsGravel);
        foreach(bool bales in new[]{false,true})
        {
            progress?.Invoke(bales?96:92,bales?"Converting movable hay bales":"Converting movable traffic signs");
            string furniture=Path.Combine(work,bales?"bales":"signs");
            PikesRoadObjects.TrafficSigns(rally,game,furniture,bales,profile,output);
            foreach(string name in new[]{"objects.pssg","objecttypes.pssg"})File.Copy(Path.Combine(furniture,name),Path.Combine(output,"shared",name),true);
            foreach(string name in new[]{"objects.ens","routesplit.pssg","ornaments.xml","ornaments.bin"})File.Copy(Path.Combine(furniture,name),Path.Combine(output,name),true);
        }
        var finalRoute=Files.Pssg(Path.Combine(output,"routesplit.pssg"));
        // Furniture conversion removes baked colour/depth by their original IDs.
        // Batch only after those objects have become independent movable draws.
        var depth=PikesDepthBatching.Apply(finalRoute);
        Files.Json(Path.Combine(output,"depth-batching.json"),depth);
        ShaderFormats.Validate(finalRoute);
        var packed=PikesIndexPacking.Apply(finalRoute);
        ReplacePssg(finalRoute,Path.Combine(output,"routesplit.pssg"));
        Files.Json(Path.Combine(output,"index-packing.json"),new{packed.Buffers,packed.BytesSaved,ExactReadback=true});
        Files.Json(Path.Combine(output,"inputs.json"),scene.Inputs);
        Files.Json(Path.Combine(output,"validation.json"),new{Passed=true,RuntimeValidated=false,PerformanceAssets=performanceAssets,SourceVenue=profile.Venue,Condition=condition,LengthMetres=scene.Length,CollisionChunks=chunks});
        progress?.Invoke(100,"Layout assets verified");
        Console.WriteLine("Pikes Peak source assets generated: "+output);
    }
    static void CopyMissing(string source,string target)
    {
        if(File.Exists(target))return;
        // These private build copies are edited later; do not inherit read-only source attributes.
        using var input=File.OpenRead(source);
        using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write);
        input.CopyTo(output);
    }
    // Only used inside this newly created candidate, never on an installed game.
    internal static void ReplacePssg(EgoEngineLibrary.Graphics.Pssg.PssgFile file,string path)
    {using var stream=File.Create(path);file.Save(stream);}
    internal static void ReplaceXml(XDocument file,string path)
    {string temporary=path+".new";Files.Xml(file,temporary);File.Move(temporary,path,true);}
    static void AddCanopyShadows(EgoEngineLibrary.Graphics.Pssg.PssgFile route)
    {
        var xml=XDocument.Parse(PikesResources.Text("material-contracts.xml"));
        var source=new XElement(xml.Root!.Elements("SHADERGROUP").Single(g=>(string?)g.Attribute("id")=="pikes_foliage.fx"));source.SetAttributeValue("id","pikes_foliage_shadow55.fx");
        var parent=route.Elements<PssgShaderGroup>().First().ParentElement!;
        var shadow=(PssgShaderGroup)EgoEngineLibrary.Graphics.Pssg.PssgElement.ReadXml(source,route,parent);parent.AppendChild(shadow);
        var shaders=new Dictionary<string,string>();
        var billboard=route.Elements<PssgShaderGroup>().SingleOrDefault(g=>g.Id==PikesTreeBillboards.Name+".fx");
        if(billboard is not null)
        {
            var group=(PssgShaderGroup)billboard.DeepClone();group.Id=PikesTreeBillboards.Name+"_shadow.fx";parent.AppendChild(group);
            foreach(var material in route.Elements<PssgShaderInstance>().Where(m=>m.ShaderGroup=="#"+billboard.Id).ToArray())
            {
                var clone=(PssgShaderInstance)material.DeepClone();clone.Id+="_billboard_shadow";clone.ShaderGroup="#"+group.Id;
                material.ParentElement!.AppendChild(clone);shaders.Add("#"+material.Id,"#"+clone.Id);
            }
        }
        foreach(var material in route.Elements<PssgShaderInstance>().Where(m=>m.ShaderGroup is "#pikes_canopy_facing.fx" or "#pikes_canopy_sheet.fx").ToArray())
        {
            var clone=(PssgShaderInstance)material.DeepClone();clone.Id+="_shadow55";clone.ShaderGroup="#"+shadow.Id;
            foreach(var extra in clone.Inputs.Where(i=>i.ParameterId>=shadow.ParameterCount).ToArray())clone.RemoveChild(extra);
            clone.ParameterCount=shadow.ParameterCount;clone.ParameterSavedCount=(uint)clone.Inputs.Count();
            material.ParentElement!.AppendChild(clone);shaders.Add("#"+material.Id,"#"+clone.Id);
        }
        int count=0;
        foreach(var node in route.Elements<PssgRenderNode>().Where(n=>n.Id.StartsWith("HIGH_")||n.Id.StartsWith("LOW_")).ToArray())
        foreach(var leaf in node.RenderInstances.Where(d=>shaders.ContainsKey(d.Shader)).ToArray())
        {
            var batch=node.ParentElement!.ChildElements.OfType<PssgRenderNode>().Single(n=>n.Id==node.Id.Insert(node.Id.IndexOf('_'),"BATCH"));
            var clone=(PssgRenderStreamInstance)leaf.DeepClone();clone.Id+="_shadow55";clone.Shader=shaders[leaf.Shader];batch.AppendChild(clone);count++;
        }
        if(count==0)throw new InvalidDataException("No gravel canopy shadow draws");
        Console.WriteLine("Native canopy shadow draws: "+count);
    }
}
