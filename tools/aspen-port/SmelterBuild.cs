using System.Xml.Linq;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.TrackQuadTree;

// Staged Smelter layout candidate. No installation, session preparation or game launch.
// Keep venue-specific reconstruction separate from the accepted Aspen pipeline.
internal static class SmelterBuild
{
    internal static void Run(string d3, string d2, string schema, string output, Action<string>? progress = null, SmelterLayout? selectedLayout = null)
    {
        PortFiles.NewOutput(output,d3,d2);
        Entities.RegisterSchema(linkedEntities:true);
        var layout=selectedLayout ?? SmelterLayout.CountyLoop;
        var condition=layout.Lighting;
        var source=Path.Combine(d3,"tracks/locations/usa/smelter");
        var sourceRoute=Path.Combine(source,layout.Route);
        var track=Path.Combine(output,"track");var route=Path.Combine(track,"route_0");Directory.CreateDirectory(route);
        var inputs=Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories).Order().ToDictionary(p=>p,PortFiles.Hash);
        var templates=ShaderTemplates.Load(d2,inputs);
        var shaders=new List<object>();var copies=new List<object>();var placements=new List<object>();
        var entityPath=Path.Combine(sourceRoute,"objects.ens");
        var entity=PortFiles.ReadPssg(entityPath);
        var originalLinks=entity.Descendants("TEMPLATEENTITYLINK").Select(e=>e.ToString(SaveOptions.DisableFormatting)).ToArray();
        var ornamentPath=Path.Combine(sourceRoute,"ornaments.bin");
        var dryOrnaments=SmelterPlacements.Prepare(File.ReadAllBytes(ornamentPath),entity,condition.Suffix == "wet",out var conditionProps);
        PortFiles.Json(Path.Combine(output,"condition-props.json"),conditionProps);
        var ornaments=Placements.Convert(dryOrnaments,false,placements,entity);
        progress?.Invoke(layout.Name + ": converting scene assets and materials");
        void Copy(string path,string target)
        {
            inputs[path]=PortFiles.Hash(path);
            string name=Path.GetFileName(path);
            if(Path.GetExtension(path) is ".pssg" or ".ens") {
                var doc=path==entityPath ? entity : PortFiles.ReadPssg(path);
                bool entities=Path.GetExtension(path)==".ens" || name=="objecttypes.pssg";
                if(entities) {
                    Entities.Convert(doc,name=="objecttypes.pssg");
                    // Native DiRT 2 equivalents for two DiRT 3-only shared
                    // physics names. Keep the fence constraints and block mesh.
                    foreach (var attribute in doc.Descendants().Attributes("props"))
                        attribute.Value = attribute.Value switch {
                            "physparams.xml#cLightPostBase" => "physparams.xml#cMediumPostBase",
                            "physparams.xml#sConcrete" => "physparams.xml#sStone",
                            _ => attribute.Value
                        };
                }
                ShaderConversion.Convert(doc,templates,Path.GetRelativePath(output,target),shaders,SmelterMaterials.Aliases);
                if(name=="trees.pssg") TreeVertexLayouts.Validate(doc);
                PortFiles.WritePssg(doc,target);
                var saved=PortFiles.ReadPssg(target);
                if(entities) Entities.Verify(doc,saved);
                if(name=="trees.pssg") TreeVertexLayouts.Validate(saved);
            }
            else if(path==ornamentPath) File.WriteAllBytes(target,ornaments);
            else if(name=="trees.bin") File.WriteAllBytes(target,Placements.Convert(File.ReadAllBytes(path),true,placements));
            else if(name=="ai_track.xml") {
                var doc=PortFiles.ReadXml(path);AiTrack.Convert(doc);
                PortFiles.WriteXml(doc,target,EgoEngineLibrary.Xml.XmlType.BinXml);
                if(!XNode.DeepEquals(doc,PortFiles.ReadXml(target))) throw new InvalidDataException("AI serialization changed.");
            }
            else if(name=="replay_camera_config.xml") PortFiles.WriteXml(ReplayCameras.Convert(PortFiles.ReadXml(path)),target,EgoEngineLibrary.Xml.XmlType.BinXml);
            else if(name is "route_overrides.xml" or "ornament_attributes.xml") {
                var doc=PortFiles.ReadXml(path);
                if(name=="route_overrides.xml") SmelterVisibility.Route(doc);
                // Ornament ranges are completed from the converted placement inventory below.
                PortFiles.WriteXml(doc,target,EgoEngineLibrary.Xml.XmlType.BinXml);
                if(!XNode.DeepEquals(doc.Root,PortFiles.ReadXml(target).Root)) throw new InvalidDataException("Smelter visibility settings changed on serialization.");
            }
            else if(name is "cloth.xml" or "lod_overrides.xml" or "tree_attributes.xml")
                PortFiles.WriteXml(PortFiles.ReadXml(path),target,EgoEngineLibrary.Xml.XmlType.BinXml);
            else PortFiles.CopyNew(path,target);
            copies.Add(new {Source=path,Target=Path.GetRelativePath(output,target)});
        }
        foreach(var root in new[]{source,sourceRoute})
        foreach(var path in Directory.EnumerateFiles(root).Order(StringComparer.Ordinal)) {
            string name=Path.GetFileName(path);
            if(!condition.Include(name) || name is "track.jpk" or "crowd_standing2.bin") continue;
            Copy(path,Path.Combine(root==source ? track : route,name));
        }
        foreach(var pair in new[]{($"{layout.Route}/sky_{condition.Suffix}.pssg","route_0/sky.pssg"),($"{layout.Route}/sky_{condition.Suffix}.pssg","sky.pssg"),($"lighting_{condition.Suffix}.xml","lighting.xml"),($"bouncemap_{condition.Suffix}.clm","baked_lights.clm"),($"{layout.Route}/shadow_map_{condition.Suffix}.clm","route_0/route.clm")})
            Copy(Path.Combine(source,pair.Item1),Path.Combine(track,pair.Item2));
        foreach(var pair in new[]{("sponsor_sparco.pssg","sponsor_pack_a.pssg"),("sponsor_slime.pssg","sponsor_pack_b.pssg"),("sponsor_yokohama.pssg","sponsor_pack_c.pssg"),("sponsor_quaife.pssg","sponsor_pack_d.pssg")})
            Copy(Path.Combine(d3,"tracks/sponsors",pair.Item1),Path.Combine(track,pair.Item2));
        foreach(var name in new[]{"dev_ai_track.xml","organism_track_dataset.xml"})
            Copy(Path.Combine(d2,"tracks/london/battersea/route_1",name),Path.Combine(route,name));
        PortFiles.Json(Path.Combine(output,"crowds.json"),Crowds.Restore(track,source,d2,inputs,sourceRoute:layout.Route,includeDayOnly:true,venueAliases:SmelterCrowds.Aliases));
        if(!originalLinks.SequenceEqual(PortFiles.ReadPssg(Path.Combine(route,"objects.ens")).Descendants("TEMPLATEENTITYLINK").Select(e=>e.ToString(SaveOptions.DisableFormatting))))
            throw new InvalidDataException("Linked barrier identities changed.");
        PortFiles.Json(Path.Combine(output,"water.json"),SmelterWater.Build(track,d2,output,inputs,condition.Suffix));
        if (condition.Suffix == "wet") SmelterWetObjects.Merge(track);
        var barrierPath = Path.Combine(track,"objects.pssg");
        var barrierMeshes = PortFiles.ReadPssg(barrierPath);
        PortFiles.Json(Path.Combine(output,"barrier-meshes.json"),SmelterBarrierMeshes.Convert(barrierMeshes));
        PortFiles.Json(Path.Combine(output,"log-meshes.json"),SmelterLogMeshes.Convert(barrierMeshes));
        PortFiles.WritePssg(barrierMeshes,barrierPath+".tmp");
        ObjectVertexLayout.Verify(barrierMeshes,PortFiles.ReadPssg(barrierPath+".tmp"));
        File.Move(barrierPath+".tmp",barrierPath,true);
        // Concrete barriers and log piles have static and movable instances. Keep
        // their native batches populated, as for Aspen's mixed plastic barriers;
        // baking/clearing the static part can hide nearby movable instances.
        PortFiles.Json(Path.Combine(output,"scene-props.json"),StaticProps.Bake(track,
            SmelterBarrierMeshes.Models.Concat(SmelterLogMeshes.Models).ToHashSet(StringComparer.Ordinal)));
        PortFiles.Json(Path.Combine(output,"ornament-visibility.json"),SmelterVisibility.Objects(track,d2,output,inputs));
        PortFiles.Json(Path.Combine(output,"terrain-containers.json"),TerrainContainers.Convert(track));
        var landPath=Path.Combine(track,"land.pssg");var land=PortFiles.ReadPssg(landPath);
        int refitted=SmelterVisibility.SceneBounds(land);
        PortFiles.WritePssg(land,landPath+".tmp");
        ObjectVertexLayout.Verify(land,PortFiles.ReadPssg(landPath+".tmp"));File.Move(landPath+".tmp",landPath,true);
        PortFiles.Json(Path.Combine(output,"scene-bounds.json"),new { RefittedParents=refitted, GeometryPreserved=true, RuntimeValidated=false });
        PortFiles.Json(Path.Combine(output,"terrain-visibility.json"),TerrainVisibility.Convert(track,source,TerrainVisibility.Mappings(System.Text.Json.JsonSerializer.SerializeToElement(placements)),layout.Route));
        PortFiles.Json(Path.Combine(output,"day-textures.json"),DayTextures.Resolve(track,condition));
        PortFiles.Json(Path.Combine(output,"terrain-occlusion.json"),TerrainOcclusion.Convert(track));
        progress?.Invoke(layout.Name + ": rebuilding and verifying ground collision");
        using var input=PortFiles.OpenRead(Path.Combine(sourceRoute,"track.jpk"));var archive=new JpkFile();archive.Read(input);
        var ground=TrackGroundGltfConverter.Convert(TrackGround.Load(archive));
        var before=Path.Combine(output,"ground.glb");ground.SaveGLB(before);
        var converted=GltfTrackGroundConverter.Convert(ground,VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
        using(var saved=new FileStream(Path.Combine(route,"track.jpk"),FileMode.CreateNew)) converted.Save().Write(saved);
        using var verify=PortFiles.OpenRead(Path.Combine(route,"track.jpk"));var readback=new JpkFile();readback.Read(verify);
        var after=Path.Combine(output,"ground-readback.glb");TrackGroundGltfConverter.Convert(TrackGround.Load(readback)).SaveGLB(after);
        var a=GeometryCheck.Read(before);var b=GeometryCheck.Read(after);
        int missing=GeometryCheck.Unmatched(a,b),added=GeometryCheck.Unmatched(b,a);
        PortFiles.Json(Path.Combine(output,"geometry.json"),new{InputTriangles=a.Count,OutputTriangles=b.Count,Missing=missing,Added=added,ToleranceMetres=.02,SurfaceAndWindingChecked=true});
        if(missing!=0 || added!=0) throw new InvalidDataException(layout.Name + " collision changed.");
        var surfaces=Path.Combine(d2,"surface_materials.xml");inputs[surfaces]=PortFiles.Hash(surfaces);
        var codes=a.Select(t=>t.Material).Distinct().Order().ToArray();
        PortFiles.WriteXml(SmelterSurfaces.Create(PortFiles.ReadXml(surfaces),codes),Path.Combine(output,"surface_materials.xml"));
        PortFiles.Json(Path.Combine(output,"surfaces.json"),codes.Select(c=>new{Code=c,Donor=SmelterSurfaces.Donors.GetValueOrDefault(c[..3],c[..3])+c[3]}).ToArray());
        inputs[schema]=PortFiles.Hash(schema);
        float length=AspenLayout.RouteLength(PortFiles.ReadXml(Path.Combine(route,"progress_track.xml")));
        TrackMetadata.Create(d2,schema,Path.Combine(output,"session-metadata"),new(layout.Directory,layout.StringId,layout.Name.ToUpperInvariant(),"smelter","SMELTER",layout.DonorModel),length,false,inputs);
        foreach(var pair in inputs) if(PortFiles.Hash(pair.Key)!=pair.Value) throw new IOException("Source changed during conversion: "+pair.Key);
        PortFiles.Json(Path.Combine(output,"inputs.json"),inputs);
        PortFiles.Json(Path.Combine(output,"shaders.json"),shaders);PortFiles.Json(Path.Combine(output,"copies.json"),copies);PortFiles.Json(Path.Combine(output,"placements.json"),placements);
        var hashes=Directory.EnumerateFiles(track,"*",SearchOption.AllDirectories).Order().ToDictionary(p=>Path.GetRelativePath(track,p),PortFiles.Hash);
        PortFiles.Json(Path.Combine(output,"candidate.json"),new{Schema=1,TrackId=layout.Id,SourceRoute=layout.Route,TrackDirectory=layout.Directory,Condition=condition.Name,Discipline=layout.Discipline,CompetitiveHeadToHead=false,Length=length,LinkedBarriers=originalLinks.Length,Files=hashes,RuntimeValidated=false,DistributionReady=false});
        progress?.Invoke(layout.Name + " staged; preparing installation receipt");
    }
}
