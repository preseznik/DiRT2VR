using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

internal sealed class PikesScene
{
    readonly Scene scene = new();
    readonly Dictionary<string, PssgFile> assets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Material> materials = new(StringComparer.Ordinal);
    readonly Dictionary<string,Material> fullMaterialStates=new(StringComparer.Ordinal);
    readonly List<object> materialAliases=[];
    readonly List<object> omissions = [];
    readonly List<object> placements = [];
    readonly List<object> physicsPlacements = [];
    readonly Dictionary<string,int> physicsModelTriangles=[];
    readonly HashSet<string> omittedRigidCollisionModels=[];
    readonly List<object> textureBindings=[];
    readonly HashSet<string> unsupportedMaterials=[];
    readonly HashSet<string> bakedPlacements=[];
    int duplicatePlacements;
    int terrainTriangles;
    PikesTerrain? terrainMaterials;
    PikesGravelMaterials? gravelMaterials;
    readonly string venue;
    readonly string sourceGame;
    readonly string route;
    readonly PikesSourceProfile profile;
    string staging = "";
    Gate[] corridor = [];
    readonly Dictionary<Cell,List<Gate>> corridorCells=[];
    readonly Dictionary<Cell,List<(Gate First,Gate Last)>> corridorSegments=[];
    int meshNumber;
    bool foliageOnly;
    bool facingRadialNormals;
    bool rigidFoliageOnly;
    bool performanceAssets;
    readonly List<object> sceneryDetails=[];
    int treePlacements,billboardPlacements;
    readonly Dictionary<string,int> billboardModels=[];
    readonly Dictionary<string,Vector2> treeDimensions=[];
    bool roadEnvelope;
    float propRadius=25;
    float roadRadius=35;
    bool continuousCorridor;
    PikesScene(string game, int routeIndex, PikesSourceProfile? profile = null)
    {
        this.profile = profile ?? PikesSourceProfile.Modern;
        sourceGame=game;
        if(routeIndex is not (0 or 2 or 4 or 6))throw new ArgumentException("Supported Pikes layouts are route_0, route_2, route_4 and route_6");
        venue = this.profile.PathIn(game); route = "route_" + routeIndex;
        scene.RoadSurfaces.Add("RDT*");
        if(this.profile.IsGravel)scene.RoadSurfaces.UnionWith(["HDG+","HDG*","MDG+","MDG*","LDG+","LDG*","DRT+","DRT*"]);
        // The original summit finish/runout uses a medium-ground apron.
        if(routeIndex==6)scene.RoadSurfaces.UnionWith(["MDG+","MDG*"]);
    }
    static float F(string text) => float.Parse(text, CultureInfo.InvariantCulture);
    static float[] Floats(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
    static Vector3 V(string text) { var f = Floats(text); if (f.Length != 3) throw new InvalidDataException("Expected float3"); return new(f[0], f[1], f[2]); }
    string Input(string relative)
    {
        string path = Files.Inside(venue, relative); Files.NoLinks(path);
        scene.Inputs.TryAdd(path, Files.Hash(path)); return path;
    }
    PssgFile Asset(string relative)
    {
        if (!assets.TryGetValue(relative, out var asset)) assets.Add(relative, asset = ReadAsset(Input(relative)));
        return asset;
    }
    PssgFile ReadAsset(string path)
    {
        using var input=File.OpenRead(path);bool gzip=input.ReadByte()==0x1f;input.Position=0;
        if(!gzip)return PssgFile.Open(input);
        using var scratch=new FileStream(Path.Combine(staging,Guid.NewGuid()+".scratch"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose);
        using(var decoder=new System.IO.Compression.GZipStream(input,System.IO.Compression.CompressionMode.Decompress,true))decoder.CopyTo(scratch);
        scratch.Position=0;return PssgFile.Open(scratch);
    }
    bool Near(Vector3 position, float radius = 110)
    {
        var cell=Scene.Tile(position);int reach=(int)MathF.Ceiling(radius/Scene.TileSize);
        if(continuousCorridor)
        {
            for(int x=-reach;x<=reach;x++)for(int z=-reach;z<=reach;z++)
                if(corridorSegments.TryGetValue(new(cell.X+x,cell.Z+z),out var segments))
                    foreach(var (first,last) in segments)
                    {
                        var a=new Vector2(first.Position.X,first.Position.Z);var edge=new Vector2(last.Position.X,last.Position.Z)-a;
                        float t=edge.LengthSquared()>1e-8f?Math.Clamp(Vector2.Dot(new Vector2(position.X,position.Z)-a,edge)/edge.LengthSquared(),0,1):0;
                        if(Vector2.DistanceSquared(new(position.X,position.Z),a+edge*t)<=radius*radius && MathF.Abs(position.Y-(first.Position.Y+(last.Position.Y-first.Position.Y)*t))<180)return true;
                    }
            return false;
        }
        for(int x=-reach;x<=reach;x++)for(int z=-reach;z<=reach;z++)
            if(corridorCells.TryGetValue(new(cell.X+x,cell.Z+z),out var gates))
                foreach(var g in gates)if((g.Position.X-position.X)*(g.Position.X-position.X)+(g.Position.Z-position.Z)*(g.Position.Z-position.Z)<=radius*radius && MathF.Abs(g.Position.Y-position.Y)<180)return true;
        return false;
    }
    internal static Scene Read(string game, string dirt2, string output, float length, bool fullCourse=false, int routeIndex=0,float? groundRadius=null,float obstacleRadius=25,float roadRadius=35,PikesSourceProfile? profile=null,PikesGravelMaterials? gravelMaterials=null,bool performanceAssets=false)
    {
        if (!fullCourse && routeIndex==0 && (!float.IsFinite(length) || length is < 500 or > 1000)) throw new ArgumentException("Prototype length must be 500â€“1000 metres");
        var reader = new PikesScene(game,routeIndex,profile){roadEnvelope=routeIndex!=0||fullCourse,propRadius=obstacleRadius,roadRadius=roadRadius,continuousCorridor=fullCourse};reader.staging=output;
        reader.scene.FullCourse=fullCourse;
        reader.gravelMaterials=gravelMaterials;
        reader.performanceAssets=performanceAssets;
        if(fullCourse)reader.scene.RoadSurfaces.UnionWith(["MDG+","MDG*"]);
        reader.scene.RoadSurfaces.UnionWith(["COB+","COB*","CON+","CON*","SDT+","SDT*"]);
        reader.Route(length,output);
        reader.Ground(dirt2,groundRadius);
        Console.WriteLine($"Collision: {reader.scene.Collision.Count:N0} triangles; origin {reader.scene.Origin}");
        var nearRoad=reader.scene.Collision.Where(t=>reader.scene.RoadSurfaces.Contains(t.Material) && new[]{t.Position0,t.Position1,t.Position2}.Any(p=>p.X*p.X+p.Z*p.Z<900)).ToArray();
        Console.WriteLine($"Road near start: {nearRoad.Length}; heights {string.Join(",",nearRoad.Take(6).Select(t=>t.Position0.Y))}");
        Files.Json(Path.Combine(output,"start-ground.json"),reader.scene.Collision.Where(t=>new[]{t.Position0,t.Position1,t.Position2}.Any(p=>p.X*p.X+p.Z*p.Z<900)).Select(t=>new{t.Material,A=new[]{t.Position0.X,t.Position0.Y,t.Position0.Z},B=new[]{t.Position1.X,t.Position1.Y,t.Position1.Z},C=new[]{t.Position2.X,t.Position2.Y,t.Position2.Z}}));
        Console.WriteLine("Reading terrain and adapting native material layouts...");
        var terrain = reader.Asset("tracksplit.pssg");
        var terrainParts=new List<PssgFile>{terrain};
        // The upper sector has only the shared terrain library; its original
        // source inventory contains no route-local routesplit.pssg.
        if(File.Exists(Files.Inside(reader.venue,reader.route+"/routesplit.pssg")))terrainParts.Add(reader.Asset(reader.route+"/routesplit.pssg"));
        reader.terrainMaterials=new PikesTerrain(dirt2,terrainParts.SelectMany(f=>f.Elements<PssgShaderGroup>()).Select(g=>g.Id).Where(id=>gravelMaterials is null ? id.StartsWith("terrain_",StringComparison.Ordinal)||id=="decal_ao_vc_flat.fx" : id.StartsWith("decal_ao",StringComparison.Ordinal)),reader.scene.Inputs);
        Console.WriteLine("Native layouts ready; extracting source terrain...");
        int examinedDraws=0;
        foreach (var terrainPart in terrainParts)
        foreach (var node in terrainPart.Elements<PssgRenderNode>().Where(n =>
            (n.Id.StartsWith("NONLOD", StringComparison.Ordinal) || n.Id.StartsWith("HIGH_", StringComparison.Ordinal) || n.Id.StartsWith("SHADOWCASTING_", StringComparison.Ordinal) || n.Id.StartsWith("DECAL_", StringComparison.Ordinal)) && !n.Id.Contains("BATCH", StringComparison.Ordinal)))
        {
            if (node.Transform.Transform != Matrix4x4.Identity) throw new InvalidDataException("Unexpected terrain render transform: " + node.Id);
            foreach (var draw in node.ChildElements.OfType<PssgRenderStreamInstance>())
            {
                reader.Draw(draw, Matrix4x4.Identity, "terrain:"+node.Id, false,fullCourse);
                if(++examinedDraws%100==0)Console.WriteLine($"Terrain draws examined: {examinedDraws}; retained triangles: {reader.terrainTriangles:N0}");
            }
        }
        Console.WriteLine($"Terrain selected: {reader.terrainTriangles:N0} triangles. Reading placed scenery...");
        reader.PlacementFile(reader.route+"/trees.bin", "trees.pssg", true);
        reader.PlacementFile(reader.route+"/ornaments.bin", "objects.pssg", false);
        reader.Entities();
        Files.Json(Path.Combine(output,"tree-detail.json"),new{PerformanceAssets=performanceAssets,TreePlacements=reader.treePlacements,BillboardPlacements=reader.billboardPlacements,
            FullDetailFallbackPlacements=reader.treePlacements-reader.billboardPlacements,Models=reader.billboardModels,TransitionMetres=performanceAssets?PikesTreeBillboards.Distance:1200,PlacementsRemoved=0});
        if(performanceAssets)Files.Json(Path.Combine(output,"scenery-detail.json"),new{AuthoredSourceModelsOnly=true,Placements=reader.sceneryDetails});
        var combined = VisualBatches.Combine(reader.scene.Visuals).ToArray();
        reader.scene.Visuals.Clear(); reader.scene.Visuals.AddRange(combined);
        foreach (var triangle in reader.scene.Collision) reader.scene.Surfaces[triangle.Material] = reader.scene.Surfaces.GetValueOrDefault(triangle.Material)+1;
        Files.Json(Path.Combine(output, "source-adapter.json"), new { Source="DiRT Rally Pikes Peak "+reader.route, reader.placements, reader.omissions,
            DiffuseOnlyMaterials=false, DiffuseOnlyProps=true, SourceVertexNormalsAndUVsRetained=true, StaticPlacementTransformsBaked=true,
            PatchupObjectTextures=true, StaticPrimitiveCollision=false, DuplicatePlacementsSkipped=reader.duplicatePlacements,
            CrossLibraryPrimitiveTemplates=false, UnsupportedLightShaftsOmitted=true, DecalCoverageRetained=true, FullTerrainScenery=fullCourse, TerrainVisualTriangles=reader.terrainTriangles,
            NativeTerrainMaterials=true, VistaUsesPrimaryDiffuseOnly=true, TerrainRouteLightmapsResolved=true,
            NativeDecalShadows=reader.scene.Visuals.Any(m=>m.Material.Native?.Group.Attribute("id")?.Value=="decal_ao.fx"),
            NativeDecalRenderLists=reader.scene.Visuals.Any(m=>m.Material.Native?.Group.Attribute("id")?.Value=="decal_ao.fx"), ExplicitUnusedRoadLayers=true,
            DynamicMotionReproduced=false, CrowdAndClothIncluded=false, RuntimeValidated=false });
        Files.Json(Path.Combine(output,"texture-bindings.json"),reader.textureBindings);
        reader.terrainMaterials.Write(output);
        gravelMaterials?.Write(output);
        Files.Json(Path.Combine(output,"material-aliases.json"),new{FullCourse=fullCourse,SourceMaterialCount=reader.materials.Count,ConvertedMaterialCount=reader.scene.Visuals.Select(m=>m.Material.Id).Distinct().Count(),ExactStateComparison=true,Aliases=reader.materialAliases});
        Files.Json(Path.Combine(output,"collision-selection.json"),new{TotalTriangles=reader.scene.Collision.Count,Surfaces=reader.scene.Surfaces,
            PhysicsModels=reader.physicsModelTriangles.OrderByDescending(p=>p.Value).ToDictionary(p=>p.Key,p=>p.Value),
            OmittedRigidCollisionModels=reader.omittedRigidCollisionModels.Order().ToArray()});
        return reader.scene;
    }
    internal static Scene ReadSectorRoute(string game,string output,int routeIndex=2,bool fullCourse=false,PikesSourceProfile? profile=null)
    {
        var reader=new PikesScene(game,routeIndex,profile);
        reader.scene.FullCourse=fullCourse;
        reader.scene.RoadSurfaces.UnionWith(["COB+","COB*","CON+","CON*","SDT+","SDT*"]);
        reader.Route(0,output);
        return reader.scene;
    }
    internal static Scene ReadFoliage(string game,string output,PikesSourceProfile? profile=null,bool facingRadialNormals=false,bool rigid=false)
    {
        var reader=new PikesScene(game,0,profile){staging=output,foliageOnly=true,continuousCorridor=true,facingRadialNormals=facingRadialNormals,rigidFoliageOnly=rigid};reader.scene.FullCourse=true;
        reader.Route(0,output);reader.PlacementFile("route_0/trees.bin","trees.pssg",true);reader.Entities();
        Files.Json(Path.Combine(output,"foliage-placements.json"),new{Placements=reader.placements,DuplicatePlacementsSkipped=reader.duplicatePlacements,Inputs=reader.scene.Inputs});
        return reader.scene;
    }
    void Route(float length,string output)
    {
        using var stream = File.OpenRead(Input(route+"/progress_track.xml"));
        var doc = XDocument.Parse(new XmlFile(stream).Document.OuterXml);
        int start = (int)doc.Descendants("split").Single(e => (string?)e.Attribute("type")=="start").Attribute("gate")!;
        int finish = (int)doc.Descendants("split").Single(e => (string?)e.Attribute("type")=="finish").Attribute("gate")!;
        var raw = doc.Descendants("gate").Select(e => (Left:V(e.Element("left")!.Value),Right:V(e.Element("right")!.Value),Distance:F((string)e.Attribute("distance")!))).ToArray();
        var centres = raw.Select(g => (g.Left+g.Right)/2).ToArray();
        scene.Origin = centres[start]-Vector3.UnitY*8;
        Gate At(int i) => new(centres[i]-scene.Origin, Vector3.Normalize(centres[Math.Min(i+1,centres.Length-1)]-centres[Math.Max(0,i-1)]),
            Vector3.Distance(raw[i].Left,centres[i]), Vector3.Distance(raw[i].Right,centres[i]), raw[i].Distance-raw[start].Distance);
        if(scene.FullCourse || route!="route_0")length=raw[finish].Distance-raw[start].Distance;
        var gates = Enumerable.Range(start, raw.Length-start).Select(At).TakeWhile(g => scene.FullCourse || g.Distance<=length+80).ToArray();
        scene.Gates = gates.Where(g=>g.Distance<=length).ToArray(); scene.Length=scene.Gates[^1].Distance;
        scene.RunoutGates=gates.Where(g=>g.Distance>scene.Length).ToArray();
        corridor=Enumerable.Range(start-4,gates.Length+4).Select(At).ToArray();
        foreach(var gate in corridor)
        {
            var cell=Scene.Tile(gate.Position);if(!corridorCells.TryGetValue(cell,out var group))corridorCells.Add(cell,group=[]);group.Add(gate);
        }
        if(continuousCorridor)foreach(var (first,last) in corridor.Zip(corridor.Skip(1)))
        {
            var low=Scene.Tile(Vector3.Min(first.Position,last.Position));var high=Scene.Tile(Vector3.Max(first.Position,last.Position));
            for(int x=low.X;x<=high.X;x++)for(int z=low.Z;z<=high.Z;z++)
            {
                var cell=new Cell(x,z);if(!corridorSegments.TryGetValue(cell,out var segments))corridorSegments.Add(cell,segments=[]);segments.Add((first,last));
            }
        }
        var checkpoints=doc.Descendants("split").Where(e=>(string?)e.Attribute("type")=="time").Select(e=>(int)e.Attribute("gate")!).ToArray();
        Files.Json(Path.Combine(output,"source-route.json"),new{SourceLayout=route,FullCourse=scene.FullCourse,SourceStartGate=start,SourceFinishGate=finish,DrivingGates=scene.Gates.Length,
            RunoutGates=scene.RunoutGates.Length,LengthMetres=scene.Length,CheckpointDistances=checkpoints.Select(i=>raw[i].Distance-raw[start].Distance).Where(d=>d>0&&d<scene.Length).ToArray(),Circuit=false});
    }
    void Ground(string dirt2,float? requestedRadius=null)
    {
        string path=Files.Inside(dirt2,"surface_materials.xml");scene.Inputs[path]=Files.Hash(path);
        using var surfaces=File.OpenRead(path);
        var names=XDocument.Parse(new XmlFile(surfaces).Document.OuterXml).Descendants("MATERIAL").Select(e=>(string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
        using var input=File.OpenRead(Input(route+"/track.jpk"));var archive=new JpkFile();archive.Read(input);
        var triangles=TrackGround.Load(archive).TraverseGrid().SelectMany(c=>c.QuadTree.GetTriangles());
        var hairpin=route=="route_4"?corridor.Where(g=>g.Distance>=4800&&g.Distance<=5050).ToArray():scene.FullCourse?corridor.Where(g=>g.Distance>=11140&&g.Distance<=11395).ToArray():[];
        foreach(var triangle in triangles)
        {
            var a=triangle.Position0-scene.Origin;var b=triangle.Position1-scene.Origin;var c=triangle.Position2-scene.Origin;
            string source=triangle.Material;string family=source[..3];
            string target=family switch { "DLE"=>"DRT", "GLD"=>"LDG", "GSL"=>"GRS", "TSD"=>"SDT", "MRD"=>"MET", "TRO"=>"RDT", "TRD"=>"RDT", "GHD"=>"HDG", "GMD"=>"MDG", "RBK"=>"ROK", _=>family };
            // Prototype uses dry tarmac; exclamation suffixes have no direct DiRT 2 equivalent.
            target += family is "RDT" or "TRO" ? "+" : source[3]=='!' ? "+" : source[3].ToString();
            float radius=roadEnvelope&&scene.RoadSurfaces.Contains(target)?roadRadius:requestedRadius??(route!="route_0"?10:50);
            // Retain the source terrain around the middle sector's third-split
            // hairpin, where leaving the old narrow strip exposed missing ground.
            if(hairpin.Length>0 && new[]{a,b,c,(a+b+c)/3}.Any(p=>hairpin.Any(g=>Vector2.DistanceSquared(new(p.X,p.Z),new(g.Position.X,g.Position.Z))<400)))radius=MathF.Max(radius,20);
            if(!Near(a,radius)&&!Near(b,radius)&&!Near(c,radius)&&!Near((a+b+c)/3,radius))continue;
            if(!names.Contains(target))throw new InvalidDataException("Unknown target collision surface: "+source+" -> "+target);
            scene.CollisionMappings[source]=target;scene.Collision.Add(new(a,b,c,target));
        }
    }
    static Matrix4x4 World(PssgNode node)
    {
        var transform=node.Transform.Transform;
        for(var parent=node.ParentElement;parent is not null;parent=parent.ParentElement)
            if(parent is PssgNode n)transform*=n.Transform.Transform;
        return transform;
    }
    void PlacementFile(string relative,string models,bool trees)
    {
        byte[] bytes=File.ReadAllBytes(Input(relative));
        int I(int at)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at,4));
        float S(int at)=>BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at,4));
        if(I(0)!=0||I(8)!=1)throw new InvalidDataException("Unsupported placement layout");
        int list=I(4), refs=I(list+36), count=I(list+40), instances=I(list+44), n=I(list+48);
        // Rally adds time-of-day tree data and an extra ornament sponsor hue field.
        int refSize=trees?40:48, size=trees?80:120;
        var names=new Dictionary<int,string>();
        for(int r=0;r<count;r++){int p=refs+r*refSize;int at=I(p);names.Add(I(p+4),Encoding.ASCII.GetString(bytes,at,Array.IndexOf(bytes,(byte)0,at)-at));}
        var asset=Asset(models);var roots=asset.Elements<PssgNode>().Where(e=>e.Name=="ROOTNODE").ToDictionary(e=>e.Id);
        if(!trees)foreach(var root in Asset(route+"/route_objects.pssg").Elements<PssgNode>().Where(e=>e.Name=="ROOTNODE"))roots.Add(root.Id,root);
        int selected=0;
        for(int i=0;i<n;i++)
        {
            int p=instances+i*size;
            if(!names.ContainsKey(I(p)))throw new InvalidDataException($"Unknown {relative} reference {I(p)} at instance {i}");
            float[] f=new float[16];f[15]=1;int[] fields=[0,1,2,4,5,6,8,9,10,12,13,14];
            for(int j=0;j<12;j++)f[fields[j]]=S(p+8+j*4);
            var placement=new Matrix4x4(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]);
            if(!Near(placement.Translation-scene.Origin,145))continue;
            string name=names[I(p)];if(!roots.TryGetValue(name+" Root",out var root))throw new InvalidDataException("Missing model root: "+name);
            Bake(root,placement,name+":"+I(p+4),trees);selected++;
        }
        placements.Add(new{File=relative,SourceInstances=n,SelectedInstances=selected});
        if(!trees)omissions.Add(new{File=relative,Feature="Linked dependent props and path animation are omitted from this desktop prototype"});
    }
    void Bake(PssgNode root,Matrix4x4 placement,string label,bool trees)
    {
        // Rally's entity XML overlaps its compiled tree/ornament placement lists.
        // Match model and transform at 0.1 mm, rather than baking both copies.
        float[] matrix=[placement.M11,placement.M12,placement.M13,placement.M14,
            placement.M21,placement.M22,placement.M23,placement.M24,
            placement.M31,placement.M32,placement.M33,placement.M34,
            placement.M41,placement.M42,placement.M43,placement.M44];
        string identity=root.Id+":"+string.Join(",",matrix.Select(v=>MathF.Round(v,4).ToString("R",CultureInfo.InvariantCulture)));
        if(!bakedPlacements.Add(identity)){duplicatePlacements++;return;}
        Vector2? dimensions=null;
        if(trees&&rigidFoliageOnly){
            if(!treeDimensions.TryGetValue(root.Id,out var size)){
                float radius=0,height=0;
                foreach(var leaf in Descendants(root).OfType<PssgRenderStreamInstance>().Where(d=>d.GetShaderInstance().GetShaderGroup().Id=="foliage.fx")){
                    var vertices=new RenderDataSourceReader(leaf.GetRenderDataSource());var transform=World((PssgNode)leaf.ParentElement!);
                    for(uint v=0;v<vertices.VertexCount;v++){var p=Vector3.Transform(vertices.GetPosition(v),transform);radius=Math.Max(radius,Math.Max(Math.Abs(p.X),Math.Abs(p.Z)));height=Math.Max(height,p.Y);}
                }
                size=new(Math.Max(radius,.01f),Math.Max(height,.01f));treeDimensions[root.Id]=size;
            }
            dimensions=size;
        }
        int before=scene.Collision.Count,visualStart=scene.Visuals.Count;
        foreach(var node in Descendants(root).OfType<PssgNode>())
        {
            bool physics=node.Id.EndsWith("_physics",StringComparison.OrdinalIgnoreCase);
            if(physics&&(root.Id.Contains("haybale",StringComparison.Ordinal)||root.Id.StartsWith("pke_",StringComparison.Ordinal)&&root.Id.Contains("sign",StringComparison.Ordinal)))continue;
            // Full-course desktop checkpoint prioritizes the driving envelope.
            // Detailed decorative rigid meshes need later primitive reconstruction;
            // all their visual geometry remains present.
            if(physics&&!DrivingCollisionModel(root.Id)) { omittedRigidCollisionModels.Add(root.Id);continue; }
            bool treeHigh=trees && node.Name=="RENDERNODE" && !physics && !node.Id.EndsWith("_xs",StringComparison.OrdinalIgnoreCase) && !node.Id.EndsWith("_x2",StringComparison.OrdinalIgnoreCase);
            if(treeHigh || (!trees && node.Name=="LODVISIBLERENDERNODE") || physics)
                foreach(var draw in node.ChildElements.OfType<PssgRenderStreamInstance>())
                {
                    Draw(draw,(trees||profile.IsGravel&&!physics?World(node):root.Transform.Transform)*placement,label,physics,treeBaseY:trees?placement.M42:null,canopyOrigin:trees?placement.Translation:null,canopyDimensions:dimensions);
                }
        }
        // Keep the tested source physics meshes. Pikes has no GRID 2
        // objecttypes.pssg library for Mizu's primitive-body reconstruction.
        physicsModelTriangles[root.Id]=physicsModelTriangles.GetValueOrDefault(root.Id)+scene.Collision.Count-before;
        if(performanceAssets&&!trees&&scene.Visuals.Count>visualStart)DistantScenery(root,placement,label,visualStart);
        if(trees&&scene.Visuals.Count>visualStart)
        {
            treePlacements++;
            var nodes=Descendants(root).OfType<PssgRenderNode>().Where(n=>n.Id.EndsWith("_x2",StringComparison.Ordinal)).ToArray();
            if(performanceAssets&&nodes.Length==1&&nodes[0].RenderInstances.Count()==1)
            {
                var draw=nodes[0].RenderInstances.Single();var world=World(nodes[0])*placement;
                var geometry=PikesTreeBillboards.Geometry(draw,world,scene.Origin,label);
                if(geometry is not null)
                {
                    var source=draw.GetShaderInstance();var material=Material(source,out _);
                    var definitions=source.GetShaderGroup().InputDefinitions.ToArray();
                    var normal=source.Inputs.SingleOrDefault(i=>definitions[i.ParameterId].InputName=="TNormalMap");
                    if(material is not null&&normal is not null)
                    {
                        string? texture=Texture(source,normal.Texture,source.Id,"TNormalMap","Billboard",Vector4.One);
                        if(texture is not null)
                        {
                            var anchor=placement.Translation-scene.Origin;
                            for(int i=visualStart;i<scene.Visuals.Count;i++)scene.Visuals[i]=scene.Visuals[i] with{LodOut=PikesTreeBillboards.Distance,DetailAnchor=anchor};
                            var maps=new Dictionary<string,string>(material.Textures){["TNormalMap"]=texture};
                            scene.Visuals.Add(geometry with{Material=material with{Id=material.Id+"_billboard",Shader=PikesTreeBillboards.Name,Textures=maps},DetailAnchor=anchor});
                            billboardPlacements++;billboardModels[root.Id]=billboardModels.GetValueOrDefault(root.Id)+1;
                        }
                    }
                }
            }
        }
    }
    void DistantScenery(PssgNode root,Matrix4x4 placement,string label,int highStart)
    {
        // Use the source artist's lower meshes, not a blanket simplifier on roads,
        // signs, barriers, buildings or mountain terrain. Physics was already read.
        string[] eligible={"_rock_","_boulder_","core_suv","core_pickup","core_van","core_car","core_truck","core_tent","core_camp"};
        if(!eligible.Any(s=>root.Id.Contains(s,StringComparison.OrdinalIgnoreCase)))return;
        var nodes=Descendants(root).OfType<PssgLodVisibleRenderNode>().ToArray();
        var selected=new List<(PssgNode Node,PssgRenderStreamInstance[] Draws)>();
        foreach(var node in nodes)
        {
            var choices=node.ChildElements.OfType<PssgLodRenderInstances>().SelectMany(l=>l.InstanceLists)
                .Select(l=>l.RenderInstances.OfType<PssgRenderStreamInstance>().ToArray()).Where(d=>d.Length>0).ToArray();
            if(choices.Length==0)return;
            var draws=choices.MinBy(ds=>ds.Sum(d=>(long)new RenderDataSourceReader(d.GetRenderDataSource()).IndexCount))!;
            selected.Add((node,draws));
        }
        if(selected.Count==0)return;
        int lowStart=scene.Visuals.Count;
        long highTriangles=scene.Visuals.Skip(highStart).Sum(m=>(long)m.Indices.Length/3);
        foreach(var (node,draws) in selected)foreach(var draw in draws)
            Draw(draw,(profile.IsGravel?World(node):root.Transform.Transform)*placement,label+":distant",false);
        long lowTriangles=scene.Visuals.Skip(lowStart).Sum(m=>(long)m.Indices.Length/3);
        if(scene.Visuals.Count-lowStart!=selected.Sum(s=>s.Draws.Length)||lowTriangles==0||lowTriangles>=highTriangles)
        {scene.Visuals.RemoveRange(lowStart,scene.Visuals.Count-lowStart);return;}
        var anchor=placement.Translation-scene.Origin;
        for(int i=highStart;i<lowStart;i++)scene.Visuals[i]=scene.Visuals[i] with{LodOut=PikesTreeBillboards.Distance,DetailAnchor=anchor};
        for(int i=lowStart;i<scene.Visuals.Count;i++)scene.Visuals[i]=scene.Visuals[i] with{LodIn=PikesTreeBillboards.Distance,DetailAnchor=anchor};
        sceneryDetails.Add(new{Model=root.Id,Placement=label,HighTriangles=highTriangles,LowTriangles=lowTriangles});
    }
    static bool DrivingCollisionModel(string name)=>new[]{"barr","fence","wall","bridge","tunnel","dam","arch","gate","building","temple","armco","rock","haybale"}
        .Any(part=>name.Contains(part,StringComparison.OrdinalIgnoreCase));
    static IEnumerable<PssgElement> Descendants(PssgElement node)
    {
        yield return node;
        foreach(var child in node.ChildElements)foreach(var item in Descendants(child))yield return item;
    }
    void Entities()
    {
        var document=XDocument.Load(Input(route+"/objects.ens"));
        var objects=Asset("objects.pssg").Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE").ToDictionary(n=>n.Id);
        var routeObjects=Asset(route+"/route_objects.pssg").Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE").ToDictionary(n=>n.Id);
        var trees=Asset("trees.pssg").Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE").ToDictionary(n=>n.Id);
        var references=document.Descendants("TEMPLATEENTITYREFERENCE").ToDictionary(e=>(string)e.Attribute("id")!,e=>(string)e.Attribute("uri")!,StringComparer.Ordinal);
        int selected=0;
        foreach(var entity in document.Descendants().Where(e=>e.Name.LocalName is "TEMPLATEENTITYINSTANCE" or "TEMPLATEBASICENTITYINSTANCE"))
        {
            var transform=entity.Element("TEMPLATETRANSFORM");if(transform is null)throw new InvalidDataException("Entity without transform");
            var f=Floats(transform.Value);
            if(f.Length!=16)throw new InvalidDataException("Entity transform must have 16 elements");
            var world=new Matrix4x4(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]);
            if(!Near(world.Translation-scene.Origin,145))continue;
            string name=((string)entity.Attribute("uri")!).TrimStart('#');
            if(references.TryGetValue(name,out var reference))
            {
                name=reference[(reference.IndexOf('#')+1)..];
                if(name.EndsWith(".max",StringComparison.Ordinal))name=name[..^4];
            }
            bool tree=false;
            if(!objects.TryGetValue(name+" Root",out var root)&&!routeObjects.TryGetValue(name+" Root",out root))
            {
                if(!trees.TryGetValue(name+" Root",out root))throw new InvalidDataException("Unknown entity model "+name);
                tree=true;
            }
            Bake(root,world,"entity:"+entity.Attribute("id"),tree);selected++;
        }
        placements.Add(new{File=route+"/objects.ens",SelectedEntities=selected});
        omissions.Add(new{File=route+"/objects.ens",Feature=$"Constraints, cloth, effects and dynamic motion omitted. Selected barriers and rocks retain rigid collision within {propRadius} metres; decorative prop collision is omitted."});
    }
    Material? Material(PssgShaderInstance source,out Vector4 scale)
    {
        var group=source.GetShaderGroup();
        scale=new(1,1,0,0);
        string key=assets.Single(p=>ReferenceEquals(p.Value,source.File)).Key+"#"+source.Id;
        // Packed light-shaft/fog maps are effect inputs, not visible RGB.
        // Rendering these planes with the opaque object shader creates blue
        // sheets and black walls. Omit them until their effect is implemented.
        if(group.Id is "Object_Light_Shafts_TwoSided.fx" or "Object_Light_Shafts.fx" or "volumetrics.fx")
        {
            if(unsupportedMaterials.Add(key))omissions.Add(new{Shader=source.Id,Group=group.Id,Reason="Packed light-shaft/fog effect omitted instead of rendering an opaque plane"});
            return null;
        }
        var root=new XElement("shader");group.WriteXml(root);source.WriteXml(root);
        var definitions=root.Element("SHADERGROUP")!.Elements("SHADERINPUTDEFINITION").ToArray();
        var values=root.Element("SHADERINSTANCE")!.Elements("SHADERINPUT").ToDictionary(e=>((string?)definitions[(int)e.Attribute("parameterID")!].Attribute("name"))!,StringComparer.OrdinalIgnoreCase);
        string? diffuseName=new[]{"TDiffuseSpecMap1","TDiffuseSpecMap2","TDiffuseSpecMap3","TDiffuseSpecMap4","TDiffuseSpecMap","TDiffuseAlphaMap","TDiffuseMap","TDiffuse","DiffuseMap","TColourMap","TAmbientOcclusionMap"}.FirstOrDefault(n=>values.GetValueOrDefault(n)?.Attribute("texture") is not null);
        if(diffuseName is null){omissions.Add(new{Shader=source.Id,Group=group.Id,Reason="No diffuse map for the prototype adapter"});return null;}
        var diffuse=values[diffuseName];
        string uvName=diffuseName.StartsWith("TDiffuseSpecMap",StringComparison.Ordinal)&&char.IsDigit(diffuseName[^1])?"Map"+diffuseName[^1]+"UVScaleAndOffset":"Map1UVScaleAndOffset";
        if(values.TryGetValue(uvName,out var uv)){var f=Floats(uv.Value);scale=new(f[0],f[1],f[2],f[3]);}
        bool nativeTerrain=group.Id.StartsWith("terrain_",StringComparison.Ordinal)||group.Id=="decal_ao_vc_flat.fx" || gravelMaterials is not null && group.Id.StartsWith("decal_ao",StringComparison.Ordinal);
        if(nativeTerrain)scale=new(1,1,0,0);
        if(materials.TryGetValue(key,out var existing))return existing;
        string reference=(string)diffuse.Attribute("texture")!;
        string? textureKey=Texture(source,reference,key,diffuseName,uvName,scale);if(textureKey is null)return null;
        bool cutout=group.Id.StartsWith("decal_",StringComparison.OrdinalIgnoreCase)||group.Id.Contains("tree",StringComparison.OrdinalIgnoreCase)||group.Id.Contains("foliage",StringComparison.OrdinalIgnoreCase)||group.Id.Contains("vegetation",StringComparison.OrdinalIgnoreCase)||group.Id.Contains("grass",StringComparison.OrdinalIgnoreCase);
        var material=new Material("pikes_"+materials.Count,source.Id,cutout?"ksTree":"ksPerPixel",0,cutout,new(){{"txDiffuse",textureKey}});
        if(gravelMaterials is not null && PikesGravelMaterials.Supports(group.Id)) material=material with{Native=gravelMaterials.Convert(source,scene.Origin,material.Textures,(name,r)=>Texture(source,r,key,name,"NativeShader",new(1,1,0,0))??throw new InvalidDataException("Missing source texture "+r))};
        else if(nativeTerrain)material=material with{Native=terrainMaterials!.Convert(source,scene.Origin,material.Textures,(name,r)=>Texture(source,r,key,name,"NativeShader",new(1,1,0,0))??throw new InvalidDataException("Missing terrain texture "+r))};
        if(scene.FullCourse)
        {
            string state=MaterialState(material);
            if(fullMaterialStates.TryGetValue(state,out var canonical))
            {
                materialAliases.Add(new{SourceShader=key,SourceMaterial=material.Id,CanonicalMaterial=canonical.Id});
                material=canonical;
            }
            else fullMaterialStates.Add(state,material);
        }
        materials.Add(key,material);return material;
    }
    static string MaterialState(Material material)
    {
        // Compare converted state, including resolved texture bytes. Source IDs
        // and texture-library aliases do not change the resulting native draw.
        var state=new XElement("material",new XAttribute("shader",material.Shader),new XAttribute("blend",material.Blend),new XAttribute("alphaTest",material.AlphaTest));
        foreach(var texture in material.Textures.OrderBy(p=>p.Key,StringComparer.Ordinal))
            state.Add(new XElement("texture",new XAttribute("name",texture.Key),new XAttribute("resolved",texture.Value)));
        foreach(var property in material.Properties.OrderBy(p=>p.Key,StringComparer.Ordinal))
            state.Add(new XElement("property",new XAttribute("name",property.Key),string.Join(" ",property.Value.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)))));
        if(material.Native is {} native)
        {
            var instance=new XElement(native.Instance);instance.Attribute("id")?.Remove();
            var definitions=native.Group.Elements("SHADERINPUTDEFINITION").ToArray();
            foreach(var input in instance.Elements("SHADERINPUT"))
                if(input.Attribute("texture") is {} texture)
                {
                    string name=(string)definitions[(int)input.Attribute("parameterID")!].Attribute("name")!;
                    texture.Value=material.Textures[name];
                }
            state.Add(new XElement("native",new XAttribute("opaque",native.Opaque),new XElement(native.Group),instance));
        }
        return state.ToString(SaveOptions.DisableFormatting);
    }
    string? Texture(PssgShaderInstance source,string reference,string key,string diffuseName,string uvName,Vector4 scale)
    {
        if (reference is "@sun" or "@ambient")
        {
            var map=reference=="@sun"?gravelMaterials!.SunTexture:gravelMaterials!.AmbientTexture;
            string id="pikes_"+map.Hash;scene.Textures.TryAdd(id,map);return id;
        }
        var textureFile=source.File;int separator=reference.IndexOf('#');
        string texturePath=assets.Single(p=>ReferenceEquals(p.Value,source.File)).Key;
        if(separator<0)throw new InvalidDataException("Texture reference has no object identity: "+reference);
        if(separator>0)
        {
            string sourcePath=assets.Single(p=>ReferenceEquals(p.Value,source.File)).Key;
            string library=reference[..separator].Split('|')[0];
            texturePath=Path.Combine(Path.GetDirectoryName(sourcePath)??"",library).Replace('\\','/');
            if(!File.Exists(Files.Inside(venue,texturePath)) && File.Exists(Files.Inside(venue,library)))texturePath=library;
            if(Path.GetFileName(texturePath).StartsWith("sponsor_pack_",StringComparison.Ordinal) && !File.Exists(Files.Inside(venue,texturePath)))
            {
                string pack=Path.GetFileName(texturePath) switch {"sponsor_pack_a.pssg"=>"sponsor_pikes_peak.pssg","sponsor_pack_b.pssg"=>"sponsor_michelin.pssg","sponsor_pack_c.pssg"=>"sponsor_colorado.pssg","sponsor_pack_d.pssg"=>"sponsor_bf_goodrich.pssg",_=>throw new InvalidDataException("Unknown source sponsor slot "+texturePath)};
                string full=Files.Inside(sourceGame,"tracks/sponsors/"+pack);texturePath="@sponsors/"+pack;
                scene.Inputs.TryAdd(full,Files.Hash(full));
                if(!assets.TryGetValue(texturePath,out textureFile)){textureFile=PikesSponsorBarrier.Read(full);assets.Add(texturePath,textureFile);}
            }
            // GRID 2 patches these black placeholder libraries at runtime.
            // Resolve the exact source IDs to their populated local libraries.
            if(texturePath=="objectstextures.pssg")texturePath="patchup_ot.pssg";
            else if(texturePath==route+"/route_objectstextures.pssg")texturePath=route+"/route_patchup_ot.pssg";
            if(!texturePath.StartsWith("@sponsors/",StringComparison.Ordinal))textureFile=Asset(texturePath);
        }
        var texture=textureFile.GetObject<PssgTexture>(reference.AsMemory(separator+1));
        if(profile.IsGravel && texturePath.Contains("patchup_ot",StringComparison.Ordinal))
        {
            var sponsor=Asset("texture_sponsor_patch.pssg");
            var overrideTexture=sponsor.TryGetObject<PssgTexture>(texture.Id.AsMemory());
            if(overrideTexture is not null){texture=overrideTexture;textureFile=sponsor;texturePath="texture_sponsor_patch.pssg";}
        }
        if(texturePath=="tracksplit.pssg" && texture.Width<=4 && texture.Height<=4)
        {
            string routeId=texture.Id.Replace(".tga","_r0_"+profile.Condition+".tga",StringComparison.Ordinal);
            var lightmaps=Asset(route+"/terrain_lm_"+profile.Condition+".pssg");
            var resolved=lightmaps.Elements<PssgTexture>().SingleOrDefault(t=>t.Id==routeId);
            if(resolved is not null){texture=resolved;texturePath=route+"/terrain_lm_"+profile.Condition+".pssg";}
        }
        var dds=texture.ToDdsFile();
        bool shadowFromAlpha=source.GetShaderGroup().Id.StartsWith("decal_ao",StringComparison.Ordinal) && diffuseName=="TAmbientOcclusionMap";
        if(shadowFromAlpha)MizuShadows.FromAlpha(dds);
        using var buffer=new MemoryStream();dds.Write(buffer);byte[] bytes=buffer.ToArray();
        string hash=Convert.ToHexString(SHA256.HashData(bytes));string textureKey="pikes_"+hash;
        textureBindings.Add(new{Shader=key,Reference=reference,ResolvedFile=texturePath,Texture=texture.Id,texture.Width,texture.Height,texture.TexelFormat,DdsHash=hash,DiffuseInput=diffuseName,UVScaleInput=uvName,UVScale=new[]{scale.X,scale.Y,scale.Z,scale.W},ShadowFromSourceAlpha=shadowFromAlpha});
        scene.Textures.TryAdd(textureKey,new(bytes,hash));
        if(diffuseName=="TColourMap")gravelMaterials?.RegisterColourRange(textureKey,textureFile,texture);
        return textureKey;
    }
    void Draw(PssgRenderStreamInstance draw,Matrix4x4 world,string label,bool physics,bool fullTerrain=false,float? treeBaseY=null,Vector3? canopyOrigin=null,Vector2? canopyDimensions=null)
    {
        if(foliageOnly && (physics || !(rigidFoliageOnly?draw.GetShaderInstance().GetShaderGroup().Id is "foliage.fx" or "foliage_billboard.fx":draw.GetShaderInstance().GetShaderGroup().Id is "treesheet_foliage_norm.fx" or "facing_foliage.fx")))return;
        // Several source branch/trunk draws also use foliage.fx. They belong
        // to the preserved trunk geometry, not the canopy correction.
        if(rigidFoliageOnly&&draw.ParentElement is PssgNode rigidNode&&!rigidNode.Id.EndsWith("_fo",StringComparison.Ordinal))return;
        var reader=new RenderDataSourceReader(draw.GetRenderDataSource());
        if(reader.VertexCount==0){omissions.Add(new{Mesh=label,Draw=draw.Id,Reason="No rigid Vertex stream; skinned shader omitted"});return;}
        if(reader.VertexCount>ushort.MaxValue||reader.IndexCount%3!=0||reader.Primitive!="triangles")throw new InvalidDataException("Unsupported mesh primitive or size: "+label);
        int n=checked((int)reader.VertexCount);var positions=new Vector3[n];
        for(uint i=0;i<n;i++)positions[i]=Vector3.Transform(reader.GetPosition(i),world)-scene.Origin;
        var selected=new List<ushort>();
        for(int i=0;i<reader.IndexCount;i+=3)
        {
            uint a=reader.GetIndex(i),b=reader.GetIndex(i+1),c=reader.GetIndex(i+2);
            // Baking all distant prop physics exhausts DiRT 2's collision-object pool.
            // Preserve full scenery but keep full-course rigid collision near the road.
            float radius=physics?propRadius:110;
            if(!fullTerrain&&!Near(positions[a],radius)&&!Near(positions[b],radius)&&!Near(positions[c],radius)&&!Near((positions[a]+positions[b]+positions[c])/3,radius))continue;
            if(physics){scene.Collision.Add(new(positions[a],positions[b],positions[c],"METL"));continue;}
            selected.Add(checked((ushort)a));selected.Add(checked((ushort)b));selected.Add(checked((ushort)c));
        }
        if(physics||selected.Count==0)return;
        var material=Material(draw.GetShaderInstance(),out var uvScale);if(material is null)return;
        if(label.StartsWith("terrain:",StringComparison.Ordinal))terrainTriangles+=selected.Count/3;
        if(!Matrix4x4.Invert(world,out var inverse)||world.GetDeterminant()<=0)throw new InvalidDataException("Reflected or singular model: "+label);
        var normalMatrix=Matrix4x4.Transpose(inverse);var normals=new Vector3[n];var uv=new Vector2[n];var tangents=new Vector3[n];
        var colours=material.Native is null&&!foliageOnly?null:new Vector4[n];var binormals=material.Native is null?null:new Vector3[n];
        bool canopyData=foliageOnly&&facingRadialNormals;
        var texCoords=material.Native is null&&!foliageOnly?null:Enumerable.Range(0,reader.TexCoordSetCount+(canopyData?1:0)).Select(_=>new Vector2[n]).ToArray();
        string? colourShader=colours is null?null:draw.GetShaderInstance().GetShaderGroup().Id;
        // Compact() retains only referenced vertices. Read the expensive source
        // attributes for those vertices; values and indices remain identical.
        foreach(ushort vertex in selected.Distinct().Order())
        {
            uint i=vertex;
            var normal=Vector3.TransformNormal(reader.GetNormal(i),normalMatrix);normals[i]=normal.LengthSquared()>1e-8f?Vector3.Normalize(normal):Vector3.UnitY;
            if(facingRadialNormals && colourShader=="facing_foliage.fx")
            {
                // Rally tree_facing derives its cluster normal from the local
                // position, transformed by the tree's orientation. A billboard
                // normal pointing at the camera loses the canopy's shaded side.
                normal=Vector3.TransformNormal(reader.GetPosition(i),world);
                normals[i]=normal.LengthSquared()>1e-8f?Vector3.Normalize(normal):Vector3.UnitY;
            }
            if(rigidFoliageOnly){
                var size=canopyDimensions??throw new InvalidDataException("Missing source tree dimensions");
                // tree_foliage derives its lighting normal from the oriented
                // local position divided by tree radius/height, not a mesh normal.
                normal=(positions[i]+scene.Origin-canopyOrigin!.Value)/new Vector3(size.X,size.Y,size.X);
                normals[i]=normal.LengthSquared()>1e-8f?Vector3.Normalize(normal):Vector3.UnitY;
            }
            var t=reader.GetTangent(i);var tangent=Vector3.TransformNormal(new(t.X,t.Y,t.Z),world);tangents[i]=tangent.LengthSquared()>1e-8f?Vector3.Normalize(tangent):Vector3.UnitX;
            var sourceUV=reader.GetTexCoord(i,0);uv[i]=sourceUV*new Vector2(uvScale.X,uvScale.Y)+new Vector2(uvScale.Z,uvScale.W);
            if(colours is not null){
                var colour=reader.GetColor(i);
                // Source ARGB words are big endian. The generic colour reader
                // treats their bytes as little endian and returns G,R,A,B.
                if(canopyData)colour=new(colour.Y,colour.X,colour.W,colour.Z);
                colours[i]=PikesTerrain.Colour(colourShader!,colour);
            }
            if(binormals is not null){var b=reader.GetBinormal(i);binormals[i]=new(b.X,b.Y,b.Z);}
            if(texCoords is not null)for(int s=0;s<reader.TexCoordSetCount;s++)texCoords[s][i]=reader.GetTexCoord(i,s);
            if(canopyData)texCoords![^1][i]=new(positions[i].Y+scene.Origin.Y-(treeBaseY??world.M42),0);
            if(foliageOnly&&!rigidFoliageOnly)
            {
                if(texCoords is null||texCoords.Length<2)throw new InvalidDataException("Source foliage lacks billboard expansion offsets");
                // tree_sheet_n_shadowed expands UV1 after transforming the
                // centre into world space. Its offsets are already world units.
            }
        }
        var mesh=Scene.Compact(new Mesh(label+":"+meshNumber++,material,false,positions,normals,uv,tangents,[]){Colors=colours,TexCoords=texCoords,Binormals=binormals},selected);
        scene.Visuals.Add(mesh);
        if((label.Contains("core_brand_scaffolding_i~",StringComparison.Ordinal)||label.Contains("core_brand_blade_a~",StringComparison.Ordinal)) &&
            draw.GetShaderInstance().Inputs.Any(i=>i.Type=="texture"&&i.Texture.StartsWith("sponsor_pack_",StringComparison.Ordinal)))
        {
            var back=mesh.Indices.ToArray();for(int i=0;i<back.Length;i+=3)(back[i+1],back[i+2])=(back[i+2],back[i+1]);
            scene.Visuals.Add(mesh with{Name=mesh.Name+":back",Normals=mesh.Normals.Select(v=>-v).ToArray(),Indices=back});
        }
    }
}
