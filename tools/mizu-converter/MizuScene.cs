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

internal sealed class MizuScene
{
    readonly Scene scene = new();
    readonly Dictionary<string, PssgFile> assets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Material> materials = new(StringComparer.Ordinal);
    readonly List<object> omissions = [];
    readonly List<object> placements = [];
    readonly Dictionary<string,int> physicsModelTriangles=[];
    readonly HashSet<string> omittedRigidCollisionModels=[];
    readonly List<object> textureBindings=[];
    readonly HashSet<string> unsupportedMaterials=[];
    int terrainTriangles;
    MizuPrimitives? primitives;
    MizuTerrain? terrainMaterials;
    readonly string venue;
    Gate[] corridor = [];
    readonly Dictionary<Cell,List<Gate>> corridorCells=[];
    int meshNumber;
    MizuScene(string game) { venue = Files.Inside(game, "tracks/locations/p2p/okutama"); }
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
        if (!assets.TryGetValue(relative, out var asset)) assets.Add(relative, asset = Files.Pssg(Input(relative)));
        return asset;
    }
    bool Near(Vector3 position, float radius = 110)
    {
        var cell=Scene.Tile(position);int reach=(int)MathF.Ceiling(radius/Scene.TileSize);
        for(int x=-reach;x<=reach;x++)for(int z=-reach;z<=reach;z++)
            if(corridorCells.TryGetValue(new(cell.X+x,cell.Z+z),out var gates))
                foreach(var g in gates)if((g.Position.X-position.X)*(g.Position.X-position.X)+(g.Position.Z-position.Z)*(g.Position.Z-position.Z)<=radius*radius && MathF.Abs(g.Position.Y-position.Y)<180)return true;
        return false;
    }
    internal static Scene Read(string game, string dirt2, string output, float length, bool fullCourse=false)
    {
        if (!fullCourse && (!float.IsFinite(length) || length is < 500 or > 1000)) throw new ArgumentException("Prototype length must be 500–1000 metres");
        var reader = new MizuScene(game);
        reader.scene.FullCourse=fullCourse;
        reader.scene.RoadSurfaces.UnionWith(["COB+","COB*","CON+","CON*","SDT+","SDT*"]);
        reader.Route(length,output);
        reader.Ground(dirt2);
        Console.WriteLine($"Collision: {reader.scene.Collision.Count:N0} triangles; origin {reader.scene.Origin}");
        var nearRoad=reader.scene.Collision.Where(t=>reader.scene.RoadSurfaces.Contains(t.Material) && new[]{t.Position0,t.Position1,t.Position2}.Any(p=>p.X*p.X+p.Z*p.Z<900)).ToArray();
        Console.WriteLine($"Road near start: {nearRoad.Length}; heights {string.Join(",",nearRoad.Take(6).Select(t=>t.Position0.Y))}");
        Files.Json(Path.Combine(output,"start-ground.json"),reader.scene.Collision.Where(t=>new[]{t.Position0,t.Position1,t.Position2}.Any(p=>p.X*p.X+p.Z*p.Z<900)).Select(t=>new{t.Material,A=new[]{t.Position0.X,t.Position0.Y,t.Position0.Z},B=new[]{t.Position1.X,t.Position1.Y,t.Position1.Z},C=new[]{t.Position2.X,t.Position2.Y,t.Position2.Z}}));
        var terrain = reader.Asset("tracksplit.pssg");
        reader.terrainMaterials=new MizuTerrain(dirt2,terrain.Elements<PssgShaderGroup>().Select(g=>g.Id).Where(id=>id.StartsWith("terrain_",StringComparison.Ordinal)||id=="decal_ao_vc_flat.fx"),reader.scene.Inputs);
        foreach (var node in terrain.Elements<PssgRenderNode>().Where(n =>
            (n.Id.StartsWith("NONLOD", StringComparison.Ordinal) || n.Id.StartsWith("HIGH_", StringComparison.Ordinal) || n.Id.StartsWith("SHADOWCASTING_", StringComparison.Ordinal) || n.Id.StartsWith("DECAL_", StringComparison.Ordinal)) && !n.Id.Contains("BATCH", StringComparison.Ordinal)))
        {
            if (node.Transform.Transform != Matrix4x4.Identity) throw new InvalidDataException("Unexpected terrain render transform: " + node.Id);
            foreach (var draw in node.ChildElements.OfType<PssgRenderStreamInstance>()) reader.Draw(draw, Matrix4x4.Identity, "terrain:"+node.Id, false,fullCourse);
        }
        if(fullCourse)reader.primitives=new MizuPrimitives(reader.Input,(path,id)=>World(reader.Asset(path).GetObject<PssgNode>(id.AsMemory())),reader.Near,reader.scene);
        reader.PlacementFile("route_0/trees.bin", "trees.pssg", true);
        reader.PlacementFile("route_0/ornaments.bin", "objects.pssg", false);
        reader.Entities();
        var combined = VisualBatches.Combine(reader.scene.Visuals).ToArray();
        reader.scene.Visuals.Clear(); reader.scene.Visuals.AddRange(combined);
        foreach (var triangle in reader.scene.Collision) reader.scene.Surfaces[triangle.Material] = reader.scene.Surfaces.GetValueOrDefault(triangle.Material)+1;
        Files.Json(Path.Combine(output, "source-adapter.json"), new { Source="GRID 2 Mizu Mountain route_0", reader.placements, reader.omissions,
            DiffuseOnlyMaterials=false, DiffuseOnlyProps=true, SourceVertexNormalsAndUVsRetained=true, StaticPlacementTransformsBaked=true,
            PatchupObjectTextures=true, StaticPrimitiveCollision=fullCourse,
            CrossLibraryPrimitiveTemplates=true, UnsupportedLightShaftsOmitted=true, DecalCoverageRetained=true, FullTerrainScenery=fullCourse, TerrainVisualTriangles=reader.terrainTriangles,
            NativeTerrainLayers=true, TerrainRouteLightmapsResolved=true, NativeDecalShadows=true, NativeDecalRenderLists=true, ExplicitUnusedRoadLayers=true,
            DynamicMotionReproduced=false, CrowdAndClothIncluded=false, RuntimeValidated=false });
        Files.Json(Path.Combine(output,"texture-bindings.json"),reader.textureBindings);
        reader.primitives?.Write(output);
        reader.terrainMaterials.Write(output);
        Files.Json(Path.Combine(output,"collision-selection.json"),new{TotalTriangles=reader.scene.Collision.Count,Surfaces=reader.scene.Surfaces,
            PhysicsModels=reader.physicsModelTriangles.OrderByDescending(p=>p.Value).ToDictionary(p=>p.Key,p=>p.Value),
            OmittedRigidCollisionModels=reader.omittedRigidCollisionModels.Order().ToArray()});
        return reader.scene;
    }
    void Route(float length,string output)
    {
        using var stream = File.OpenRead(Input("route_0/progress_track.xml"));
        var doc = XDocument.Parse(new XmlFile(stream).Document.OuterXml);
        int start = (int)doc.Descendants("split").Single(e => (string?)e.Attribute("type")=="start").Attribute("gate")!;
        int finish = (int)doc.Descendants("split").Single(e => (string?)e.Attribute("type")=="finish").Attribute("gate")!;
        var raw = doc.Descendants("gate").Select(e => (Left:V(e.Element("left")!.Value),Right:V(e.Element("right")!.Value),Distance:F((string)e.Attribute("distance")!))).ToArray();
        var centres = raw.Select(g => (g.Left+g.Right)/2).ToArray();
        scene.Origin = centres[start]-Vector3.UnitY*8;
        Gate At(int i) => new(centres[i]-scene.Origin, Vector3.Normalize(centres[Math.Min(i+1,centres.Length-1)]-centres[Math.Max(0,i-1)]),
            Vector3.Distance(raw[i].Left,centres[i]), Vector3.Distance(raw[i].Right,centres[i]), raw[i].Distance-raw[start].Distance);
        if(scene.FullCourse)length=raw[finish].Distance-raw[start].Distance;
        var gates = Enumerable.Range(start, raw.Length-start).Select(At).TakeWhile(g => scene.FullCourse || g.Distance<=length+80).ToArray();
        scene.Gates = gates.Where(g=>g.Distance<=length).ToArray(); scene.Length=scene.Gates[^1].Distance;
        scene.RunoutGates=gates.Where(g=>g.Distance>scene.Length).ToArray();
        corridor=Enumerable.Range(start-4,gates.Length+4).Select(At).ToArray();
        foreach(var gate in corridor)
        {
            var cell=Scene.Tile(gate.Position);if(!corridorCells.TryGetValue(cell,out var group))corridorCells.Add(cell,group=[]);group.Add(gate);
        }
        var checkpoints=doc.Descendants("split").Where(e=>(string?)e.Attribute("type")=="time").Select(e=>(int)e.Attribute("gate")!).ToArray();
        Files.Json(Path.Combine(output,"source-route.json"),new{FullCourse=scene.FullCourse,SourceStartGate=start,SourceFinishGate=finish,DrivingGates=scene.Gates.Length,
            RunoutGates=scene.RunoutGates.Length,LengthMetres=scene.Length,CheckpointDistances=checkpoints.Select(i=>raw[i].Distance-raw[start].Distance).Where(d=>d>0&&d<scene.Length).ToArray(),Circuit=false});
    }
    void Ground(string dirt2)
    {
        string path=Files.Inside(dirt2,"surface_materials.xml");scene.Inputs[path]=Files.Hash(path);
        using var surfaces=File.OpenRead(path);
        var names=XDocument.Parse(new XmlFile(surfaces).Document.OuterXml).Descendants("MATERIAL").Select(e=>(string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
        using var input=File.OpenRead(Input("route_0/track.jpk"));var archive=new JpkFile();archive.Read(input);
        var triangles=TrackGround.Load(archive).TraverseGrid().SelectMany(c=>c.QuadTree.GetTriangles());
        foreach(var triangle in triangles)
        {
            var a=triangle.Position0-scene.Origin;var b=triangle.Position1-scene.Origin;var c=triangle.Position2-scene.Origin;
            float radius=scene.FullCourse?50:125;
            if(!Near(a,radius)&&!Near(b,radius)&&!Near(c,radius)&&!Near((a+b+c)/3,radius))continue;
            string source=triangle.Material;string family=source[..3];
            string target=family switch { "DLE"=>"DRT", "GLD"=>"LDG", "GSL"=>"GRS", "TSD"=>"SDT", "MRD"=>"MET", "TRO"=>"RDT", _=>family };
            // Prototype uses dry tarmac; exclamation suffixes have no direct DiRT 2 equivalent.
            target += family is "RDT" or "TRO" ? "+" : source[3]=='!' ? "+" : source[3].ToString();
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
        // GRID 2 adds seven words after the DiRT 3 ornament payload (116 bytes).
        int refSize=trees?40:48, size=trees?76:116;
        var names=new Dictionary<int,string>();
        for(int r=0;r<count;r++){int p=refs+r*refSize;int at=I(p);names.Add(I(p+4),Encoding.ASCII.GetString(bytes,at,Array.IndexOf(bytes,(byte)0,at)-at));}
        var asset=Asset(models);var roots=asset.Elements<PssgNode>().Where(e=>e.Name=="ROOTNODE").ToDictionary(e=>e.Id);
        if(!trees)foreach(var root in Asset("route_0/route_objects.pssg").Elements<PssgNode>().Where(e=>e.Name=="ROOTNODE"))roots.Add(root.Id,root);
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
        int before=scene.Collision.Count;
        foreach(var node in Descendants(root).OfType<PssgNode>())
        {
            bool physics=node.Id.EndsWith("_physics",StringComparison.OrdinalIgnoreCase);
            // Full-course desktop checkpoint prioritizes the driving envelope.
            // Detailed decorative rigid meshes need later primitive reconstruction;
            // all their visual geometry remains present.
            if(physics&&scene.FullCourse&&!DrivingCollisionModel(root.Id)) { omittedRigidCollisionModels.Add(root.Id);continue; }
            bool treeHigh=trees && node.Name=="RENDERNODE" && !physics && !node.Id.EndsWith("_xs",StringComparison.OrdinalIgnoreCase) && !node.Id.EndsWith("_x2",StringComparison.OrdinalIgnoreCase);
            if(treeHigh || (!trees && node.Name=="LODVISIBLERENDERNODE") || physics)
                foreach(var draw in node.ChildElements.OfType<PssgRenderStreamInstance>())
                    Draw(draw,(trees?World(node):root.Transform.Transform)*placement,label,physics);
        }
        // Source primitive bodies replace missing/filtered rigid meshes, never
        // overlay collision already retained for this placement.
        if(scene.Collision.Count==before&&primitives is not null)
            primitives.Add(root.Id[..^5],assets.Single(p=>ReferenceEquals(p.Value,root.File)).Key,placement,label);
        physicsModelTriangles[root.Id]=physicsModelTriangles.GetValueOrDefault(root.Id)+scene.Collision.Count-before;
    }
    static bool DrivingCollisionModel(string name)=>new[]{"barr","fence","wall","bridge","tunnel","dam","arch","gate","building","temple"}
        .Any(part=>name.Contains(part,StringComparison.OrdinalIgnoreCase));
    static IEnumerable<PssgElement> Descendants(PssgElement node)
    {
        yield return node;
        foreach(var child in node.ChildElements)foreach(var item in Descendants(child))yield return item;
    }
    void Entities()
    {
        var document=XDocument.Load(Input("route_0/objects.ens"));
        var objects=Asset("objects.pssg").Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE").ToDictionary(n=>n.Id);
        var routeObjects=Asset("route_0/route_objects.pssg").Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE").ToDictionary(n=>n.Id);
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
        placements.Add(new{File="route_0/objects.ens",SelectedEntities=selected});
        omissions.Add(new{File="route_0/objects.ens",Feature="Constraints, cloth, effects and dynamic motion omitted; retained rigid meshes and static source primitive bodies supply selected collision"});
    }
    Material? Material(PssgShaderInstance source,out Vector4 scale)
    {
        var group=source.GetShaderGroup();
        scale=new(1,1,0,0);
        string key=assets.Single(p=>ReferenceEquals(p.Value,source.File)).Key+"#"+source.Id;
        // Packed light-shaft/fog maps are effect inputs, not visible RGB.
        // Rendering these planes with the opaque object shader creates blue
        // sheets and black walls. Omit them until their effect is implemented.
        if(group.Id is "Object_Light_Shafts_TwoSided.fx" or "Object_Light_Shafts.fx")
        {
            if(unsupportedMaterials.Add(key))omissions.Add(new{Shader=source.Id,Group=group.Id,Reason="Packed light-shaft/fog effect omitted instead of rendering an opaque plane"});
            return null;
        }
        var root=new XElement("shader");group.WriteXml(root);source.WriteXml(root);
        var definitions=root.Element("SHADERGROUP")!.Elements("SHADERINPUTDEFINITION").ToArray();
        var values=root.Element("SHADERINSTANCE")!.Elements("SHADERINPUT").ToDictionary(e=>((string?)definitions[(int)e.Attribute("parameterID")!].Attribute("name"))!,StringComparer.OrdinalIgnoreCase);
        string? diffuseName=new[]{"TDiffuseSpecMap1","TDiffuseSpecMap2","TDiffuseSpecMap3","TDiffuseSpecMap4","TDiffuseSpecMap","TDiffuseAlphaMap","TDiffuseMap","TDiffuse","DiffuseMap","TColourMap"}.FirstOrDefault(n=>values.GetValueOrDefault(n)?.Attribute("texture") is not null);
        if(diffuseName is null){omissions.Add(new{Shader=source.Id,Group=group.Id,Reason="No diffuse map for the prototype adapter"});return null;}
        var diffuse=values[diffuseName];
        string uvName=diffuseName.StartsWith("TDiffuseSpecMap",StringComparison.Ordinal)&&char.IsDigit(diffuseName[^1])?"Map"+diffuseName[^1]+"UVScaleAndOffset":"Map1UVScaleAndOffset";
        if(values.TryGetValue(uvName,out var uv)){var f=Floats(uv.Value);scale=new(f[0],f[1],f[2],f[3]);}
        bool nativeTerrain=group.Id.StartsWith("terrain_",StringComparison.Ordinal)||group.Id=="decal_ao_vc_flat.fx";
        if(nativeTerrain)scale=new(1,1,0,0);
        if(materials.TryGetValue(key,out var existing))return existing;
        string reference=(string)diffuse.Attribute("texture")!;
        string? textureKey=Texture(source,reference,key,diffuseName,uvName,scale);if(textureKey is null)return null;
        bool cutout=group.Id.StartsWith("decal_",StringComparison.OrdinalIgnoreCase)||group.Id.Contains("tree",StringComparison.OrdinalIgnoreCase)||group.Id.Contains("foliage",StringComparison.OrdinalIgnoreCase)||group.Id.Contains("vegetation",StringComparison.OrdinalIgnoreCase)||group.Id.Contains("grass",StringComparison.OrdinalIgnoreCase);
        var material=new Material("mizu_"+materials.Count,source.Id,cutout?"ksTree":"ksPerPixel",0,cutout,new(){{"txDiffuse",textureKey}});
        if(nativeTerrain)material=material with{Native=terrainMaterials!.Convert(source,scene.Origin,material.Textures,(name,r)=>Texture(source,r,key,name,"NativeShader",new(1,1,0,0))??throw new InvalidDataException("Missing terrain texture "+r))};
        materials.Add(key,material);return material;
    }
    string? Texture(PssgShaderInstance source,string reference,string key,string diffuseName,string uvName,Vector4 scale)
    {
        var textureFile=source.File;int separator=reference.IndexOf('#');
        string texturePath=assets.Single(p=>ReferenceEquals(p.Value,source.File)).Key;
        if(separator<0)throw new InvalidDataException("Texture reference has no object identity: "+reference);
        if(separator>0)
        {
            string sourcePath=assets.Single(p=>ReferenceEquals(p.Value,source.File)).Key;
            texturePath=Path.Combine(Path.GetDirectoryName(sourcePath)??"",reference[..separator]).Replace('\\','/');
            if(!File.Exists(Files.Inside(venue,texturePath)) && File.Exists(Files.Inside(venue,reference[..separator])))texturePath=reference[..separator];
            if(Path.GetFileName(texturePath).StartsWith("sponsor_pack_",StringComparison.Ordinal) && !File.Exists(Files.Inside(venue,texturePath)))
            {
                omissions.Add(new{Shader=source.Id,Group=source.GetShaderGroup().Id,Reason="Runtime sponsor-pack texture alias omitted"});return null;
            }
            // GRID 2 patches these black placeholder libraries at runtime.
            // Resolve the exact source IDs to their populated local libraries.
            texturePath=texturePath switch { "objectstextures.pssg"=>"patchup_ot.pssg", "route_0/route_objectstextures.pssg"=>"route_0/route_patchup_ot.pssg", _=>texturePath };
            textureFile=Asset(texturePath);
        }
        var texture=textureFile.GetObject<PssgTexture>(reference.AsMemory(separator+1));
        if(texturePath=="tracksplit.pssg" && texture.Width<=4 && texture.Height<=4)
        {
            string routeId=texture.Id.StartsWith("_okutama_",StringComparison.Ordinal)?texture.Id.Replace(".tga","_r0_day.tga",StringComparison.Ordinal):texture.Id;
            var lightmaps=Asset("route_0/terrain_lm_day.pssg");
            var resolved=lightmaps.Elements<PssgTexture>().SingleOrDefault(t=>t.Id==routeId);
            if(resolved is not null){texture=resolved;texturePath="route_0/terrain_lm_day.pssg";}
        }
        var dds=texture.ToDdsFile();
        bool shadowFromAlpha=source.GetShaderGroup().Id=="decal_ao_vc_flat.fx" && diffuseName=="TAmbientOcclusionMap";
        if(shadowFromAlpha)MizuShadows.FromAlpha(dds);
        using var buffer=new MemoryStream();dds.Write(buffer);byte[] bytes=buffer.ToArray();
        string hash=Convert.ToHexString(SHA256.HashData(bytes));string textureKey="mizu_"+hash;
        textureBindings.Add(new{Shader=key,Reference=reference,ResolvedFile=texturePath,Texture=texture.Id,texture.Width,texture.Height,texture.TexelFormat,DdsHash=hash,DiffuseInput=diffuseName,UVScaleInput=uvName,UVScale=new[]{scale.X,scale.Y,scale.Z,scale.W},ShadowFromSourceAlpha=shadowFromAlpha});
        scene.Textures.TryAdd(textureKey,new(bytes,hash));
        return textureKey;
    }
    void Draw(PssgRenderStreamInstance draw,Matrix4x4 world,string label,bool physics,bool fullTerrain=false)
    {
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
            float radius=physics&&scene.FullCourse?25:110;
            if(!fullTerrain&&!Near(positions[a],radius)&&!Near(positions[b],radius)&&!Near(positions[c],radius)&&!Near((positions[a]+positions[b]+positions[c])/3,radius))continue;
            if(physics){scene.Collision.Add(new(positions[a],positions[b],positions[c],"METL"));continue;}
            selected.Add(checked((ushort)a));selected.Add(checked((ushort)b));selected.Add(checked((ushort)c));
        }
        if(physics||selected.Count==0)return;
        var material=Material(draw.GetShaderInstance(),out var uvScale);if(material is null)return;
        if(label.StartsWith("terrain:",StringComparison.Ordinal))terrainTriangles+=selected.Count/3;
        if(!Matrix4x4.Invert(world,out var inverse)||world.GetDeterminant()<=0)throw new InvalidDataException("Reflected or singular model: "+label);
        var normalMatrix=Matrix4x4.Transpose(inverse);var normals=new Vector3[n];var uv=new Vector2[n];var tangents=new Vector3[n];
        var colours=material.Native is null?null:new Vector4[n];var binormals=material.Native is null?null:new Vector3[n];
        var texCoords=material.Native is null?null:Enumerable.Range(0,reader.TexCoordSetCount).Select(_=>new Vector2[n]).ToArray();
        for(uint i=0;i<n;i++)
        {
            var normal=Vector3.TransformNormal(reader.GetNormal(i),normalMatrix);normals[i]=normal.LengthSquared()>1e-8f?Vector3.Normalize(normal):Vector3.UnitY;
            var t=reader.GetTangent(i);var tangent=Vector3.TransformNormal(new(t.X,t.Y,t.Z),world);tangents[i]=tangent.LengthSquared()>1e-8f?Vector3.Normalize(tangent):Vector3.UnitX;
            var sourceUV=reader.GetTexCoord(i,0);uv[i]=sourceUV*new Vector2(uvScale.X,uvScale.Y)+new Vector2(uvScale.Z,uvScale.W);
            if(colours is not null)colours[i]=MizuTerrain.Colour(draw.GetShaderInstance().GetShaderGroup().Id,reader.GetColor(i));
            if(binormals is not null){var b=reader.GetBinormal(i);binormals[i]=new(b.X,b.Y,b.Z);}
            if(texCoords is not null)for(int s=0;s<texCoords.Length;s++)texCoords[s][i]=reader.GetTexCoord(i,s);
        }
        scene.Visuals.Add(Scene.Compact(new Mesh(label+":"+meshNumber++,material,false,positions,normals,uv,tangents,[]){Colors=colours,TexCoords=texCoords,Binormals=binormals},selected));
    }
}
