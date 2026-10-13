using DiRT2VR.CarImport;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace DiRT2VR.CustomCars;

internal static class FerrariExterior
{
    internal sealed record Part(CarMesh Mesh, Matrix4x4 World, string Joint);
    internal static void Build(string source,string donor,string output)
    {
        source=Path.GetFullPath(source);output=Path.GetFullPath(output);
        if(Directory.Exists(output) || output==source || output.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("New output outside source required");
        BuildDetail(source,donor,output,false);
        BuildDetail(source,donor,output,true);
        FerrariWheelLayout.Build(source,donor,output);
        if(!OperatingSystem.IsWindowsVersionAtLeast(6,1))throw new PlatformNotSupportedException("Ferrari shadow conversion requires Windows.");
        FerrariAmbientShadow.Build(source,donor,output);
    }
    static void BuildDetail(string source,string donor,string output,bool low)
    {
        var high=CarKn5.Read(Path.Combine(source,"ferrari_f2004.kn5"));
        using var original=File.OpenRead(Path.Combine(donor,low?"cars/prc/prc_lowLOD.pssg":"cars/prc/prc_highLOD.pssg"));var pssg=PssgFile.Open(original);
        if(!GltfCarExteriorPssgConverter.SupportsPssg(pssg))throw new InvalidDataException("Unsupported donor exterior hierarchy");
        var oldJoints=pssg.Elements<PssgMatrixPaletteJointNode>().ToArray();
        var oldAnchors=pssg.Elements<PssgNode>().Where(n=>n.GetType()==typeof(PssgNode)).ToArray();var sourceRig=FerrariSuspension.Reference(high);
        using var textureSource=File.OpenRead(Path.Combine(donor,"cars/prc/livery_00/textures_high/prc_tex_high_00.pssg"));
        var textureTemplate=PssgFile.Open(textureSource).Elements<PssgTexture>().First();
        var root=new NodeBuilder("Scene Root");var scene=new SceneBuilder();var selected=new List<object>();var lodParts=new List<(int Lod,List<Part> Parts,Dictionary<string,Matrix4x4> Pivots,Dictionary<string,Matrix4x4> Fixed)>();var suspensionReport=new List<object>();
        var levels=low?new[]{("ferrari_f2004_lod_C.kn5",2),("ferrari_f2004_lod_D.kn5",3),("ferrari_f2004_lod_D.kn5",4)}:new[]{("ferrari_f2004.kn5",0),("ferrari_f2004_lod_B.kn5",1)};
        foreach(var (file,lod) in levels)
        {
            var model=lod==0?high:CarKn5.Read(Path.Combine(source,file));var parts=new List<Part>();var pivots=new Dictionary<string,Matrix4x4>();
            void Visit(CarNode node,Matrix4x4 world,bool enabled,string joint,bool exclude)
            {
                world=node.LocalTransform*world;enabled&=node.Active || node.Name=="COCKPIT_LR";
                if(node.Name=="COCKPIT_LR")joint="cockpit";
                exclude|=node.Name is "COCKPIT_HR" or "CINTURE_OFF" or "g_Custom_Driver" || node.Name.Contains("Blur",StringComparison.OrdinalIgnoreCase) || node.Name.Contains("Damage_Glass",StringComparison.OrdinalIgnoreCase);
                string? wheel=node.Name switch{"WHEEL_LF"=>"wheel_fl","WHEEL_RF"=>"wheel_fr","WHEEL_LR"=>"wheel_bl","WHEEL_RR"=>"wheel_br","STEER_LR"=>"steering_wheel",_=>null};
                var suspension=FerrariSuspension.Joint(node.Name);if(suspension is not null){joint=suspension;pivots[joint]=world;}
                if(wheel is not null){joint=wheel;pivots[joint]=world;}
                if(node.Name.StartsWith("TYRE_",StringComparison.Ordinal) && joint.StartsWith("wheel_",StringComparison.Ordinal))
                {
                    var rim=joint;joint="tyre_"+rim[6..];pivots[joint]=pivots[rim];
                }
                if(node.Mesh is {} mesh && enabled && !exclude && mesh.Visible && mesh.Renderable)
                {
                    var sourceMaterial=model.Materials[mesh.Material];int material=Array.FindIndex(high.Materials,m=>m.Name==sourceMaterial.Name);if(material<0)throw new InvalidDataException("LOD material mismatch");
                    parts.Add(new(mesh with{Material=material},world,joint));selected.Add(new{File=file,Node=node.Name,Joint=joint,Triangles=mesh.Indices.Length/3});
                }
                foreach(var child in node.Children)Visit(child,world,enabled,joint,exclude);
            }
            Visit(model.Root,Matrix4x4.Identity,true,"main_body",false);
            var fixedAnchors=new Dictionary<string,Matrix4x4>();suspensionReport.AddRange(FerrariSuspension.Bind(parts,pivots,fixedAnchors).Select(binding=>(object)new{Lod=lod,Binding=binding}));
            lodParts.Add((lod,parts,pivots,fixedAnchors));
        }
        var materialSet=FerrariMaterials.Build(high,source,pssg,textureTemplate,false,lodParts.SelectMany(l=>l.Parts).Select(p=>p.Mesh.Material).ToHashSet());
        var mats=materialSet.Materials;var materialReport=materialSet.MaterialReport;var textureReport=materialSet.TextureReport;
        foreach(var (lod,parts,pivots,fixedAnchors) in lodParts)
        {
            var lodNode=root.CreateNode($"LOD{lod}_");
            foreach(var group in parts.GroupBy(p=>p.Joint))
            {
                var pivot=pivots.GetValueOrDefault(group.Key,Matrix4x4.Identity);if(!Matrix4x4.Invert(pivot,out var inverse))throw new InvalidDataException("Singular native pivot");
                bool animatedWheel=group.Key.StartsWith("wheel_",StringComparison.Ordinal)||group.Key.StartsWith("tyre_",StringComparison.Ordinal);
                // Native donor wheel binds are unrotated. AC's static camber is
                // part of the source hierarchy, not the native animation bind.
                var nativePivot=animatedWheel?Matrix4x4.CreateTranslation(pivot.Translation):pivot;
                var name=$"x{lod}_{group.Key}";var node=lodNode.CreateNode(name);node.LocalTransform=nativePivot;
                var mesh=new MeshBuilder<VertexPositionNormalTangent,VertexColor1Texture4,VertexEmpty>(name);
                foreach(var part in group)
                {
                    var transform=part.World*inverse;Matrix4x4.Invert(transform,out var inv);var normalMatrix=Matrix4x4.Transpose(inv);
                    var primitive=mesh.UsePrimitive(mats[part.Mesh.Material]);
                    VertexBuilder<VertexPositionNormalTangent,VertexColor1Texture4,VertexEmpty> Vertex(int i)
                    {
                        var n=Vector3.TransformNormal(part.Mesh.Normals[i],normalMatrix);n=n.LengthSquared()>1e-10f?Vector3.Normalize(n):Vector3.UnitY;
                        var t=Vector3.TransformNormal(part.Mesh.Tangents[i],transform);t-=n*Vector3.Dot(n,t);
                        if(t.LengthSquared()<1e-10f)t=Vector3.Cross(n,Math.Abs(n.Y)<.9f?Vector3.UnitY:Vector3.UnitX);t=Vector3.Normalize(t);
                        var v=new VertexBuilder<VertexPositionNormalTangent,VertexColor1Texture4,VertexEmpty>();v.Geometry.Position=Vector3.Transform(part.Mesh.Positions[i],transform);v.Geometry.Normal=n;v.Geometry.Tangent=new(t,1);// Native donor tyres encode a zero green channel; retain that tyre-specific shader attribute.
                        v.Material.Color=group.Key.StartsWith("tyre_",StringComparison.Ordinal)?new Vector4(1,0,1,1):Vector4.One;
                        v.Material.TexCoord0=v.Material.TexCoord1=v.Material.TexCoord2=v.Material.TexCoord3=part.Mesh.UV[i];return v;
                    }
                    for(int i=0;i<part.Mesh.Indices.Length;i+=3)primitive.AddTriangle(Vertex(part.Mesh.Indices[i]),Vertex(part.Mesh.Indices[i+1]),Vertex(part.Mesh.Indices[i+2]));
                }
                scene.AddRigidMesh(mesh,node);
            }
        }
        var gltf=scene.ToGltf2();new GltfCarExteriorPssgConverter().Convert(gltf,pssg);
        // Keep donor attachment/damage joint identities that are not yet mapped,
        // without keeping their donor geometry or stale render-data references.
        var ids=pssg.Elements<PssgObject>().Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var originalJoint in oldJoints.Where(j=>!ids.Contains(j.Id)))
        {
            var parent=pssg.Elements<PssgMatrixPaletteBundleNode>().SingleOrDefault(n=>n.Id==originalJoint.ParentElement!.GetAttributeValue<string>("id"));
            // A native donor may keep empty high-LOD joint identities in its low
            // file. Retain attachments only for LOD bundles actually converted.
            if(parent is null)continue;
            var joint=(PssgMatrixPaletteJointNode)originalJoint.DeepClone();
            var rigLevel=lodParts.Single(l=>parent.Id==$"LOD{l.Lod}_");var rigName=originalJoint.Id[(originalJoint.Id.IndexOf('_')+1)..];
            if(rigLevel.Fixed.TryGetValue(rigName,out var fixedPose))joint.Transform.Transform=fixedPose;
            else if(rigName.StartsWith("hub_caliper_",StringComparison.Ordinal) && !rigName.Contains("_fixed_") && rigLevel.Pivots.TryGetValue("wheel_"+rigName[^2..],out var sourceWheel))joint.Transform.Transform=Matrix4x4.CreateTranslation(sourceWheel.Translation);
            else
            if(new[]{"hub_caliper","suspension","wishbone","disc_"}.Any(name=>originalJoint.Id.Contains(name,StringComparison.Ordinal)))
            {
                var axle=originalJoint.Id[^2..];var level=lodParts.Single(l=>parent.Id==$"LOD{l.Lod}_");
                var originalWheel=oldJoints.SingleOrDefault(j=>j.Id==originalJoint.Id[..originalJoint.Id.IndexOf('_')]+"_wheel_"+axle);
                if(originalWheel is not null && level.Pivots.TryGetValue("wheel_"+axle,out var wheel))
                {
                    // Retain the donor linkage offsets. Fixed and moving hubs must
                    // not collapse onto the same point as the wheel centre.
                    var origin=originalWheel.Transform.Transform;var anchor=joint.Transform.Transform;
                    anchor.M41*=wheel.M41/origin.M41;
                    anchor.M42=wheel.M42+(anchor.M42-origin.M42)*(.33f/.460918f);
                    anchor.M43=wheel.M43+(anchor.M43-origin.M43)*(3.05f/3.0214438f);
                    joint.Transform.Transform=anchor;
                }
            }
            foreach(var render in joint.ChildElements.OfType<PssgMatrixPaletteJointRenderInstance>().ToArray())joint.RemoveChild(render);
            parent.AppendChild(joint);
        }
        // Fixed linkage endpoints are ordinary nodes under Scene Root, not
        // matrix-palette render joints. The importer clears both kinds.
        var restoredAnchors=new List<string>();
        foreach(var originalAnchor in oldAnchors){
            if(pssg.TryGetObject<PssgNode>(originalAnchor.Id.AsMemory()) is not null)continue;
            var parentId=(originalAnchor.ParentElement as PssgObject)?.Id??throw new InvalidDataException("Missing anchor parent");
            var parent=pssg.TryGetObject<PssgNode>(parentId.AsMemory())??throw new InvalidDataException("Unresolved fixed anchor parent: "+parentId);
            var anchor=(PssgNode)originalAnchor.DeepClone();
            foreach(var child in anchor.ChildElements.OfType<PssgNode>().ToArray())anchor.RemoveChild(child);
            if(anchor.Id.StartsWith("x0_",StringComparison.Ordinal)){
                var key=anchor.Id[3..];
                if(sourceRig.Fixed.TryGetValue(key,out var sourceAnchor))anchor.Transform.Transform=sourceAnchor;
                else if(key.Contains("_fixed_",StringComparison.Ordinal)){
                    var side=key[^2..];var originalWheel=oldJoints.SingleOrDefault(j=>j.Id=="x0_wheel_"+side);
                    if(originalWheel is not null&&sourceRig.Pivots.TryGetValue("wheel_"+side,out var wheel)){
                        var origin=originalWheel.Transform.Transform;var matrix=anchor.Transform.Transform;
                        matrix.M41*=wheel.M41/origin.M41;matrix.M42=wheel.M42+(matrix.M42-origin.M42)*(.33f/.460918f);matrix.M43=wheel.M43+(matrix.M43-origin.M43)*(3.05f/3.0214438f);anchor.Transform.Transform=matrix;
                    }
                }
            }
            parent.AppendChild(anchor);restoredAnchors.Add(anchor.Id);
        }
        Directory.CreateDirectory(output);var outputFile=Path.Combine(output,low?"f04_lowLOD.pssg":"f04_highLOD.pssg");pssg.FileType=PssgFileType.Pssg;using(var target=File.Create(outputFile))pssg.WritePssg(target);
        using(var check=File.OpenRead(outputFile))
        {
            var parsed=PssgFile.Open(check);if(!GltfCarExteriorPssgConverter.SupportsPssg(parsed))throw new InvalidDataException("Generated exterior unreadable");
            foreach(var anchor in oldAnchors)if(parsed.TryGetObject<PssgNode>(anchor.Id.AsMemory()) is null)throw new InvalidDataException("Missing native anchor after conversion: "+anchor.Id);
            foreach(var joint in parsed.Elements<PssgMatrixPaletteSkinJoint>())if(parsed.TryGetObject<PssgMatrixPaletteJointNode>(joint.Joint.AsMemory(1)) is null)throw new InvalidDataException("Unresolved native skin joint");
            foreach(var input in parsed.Elements<PssgShaderInput>().Where(i=>i.Type=="texture" && i.Texture.StartsWith("#")))if(parsed.TryGetObject<PssgTexture>(input.Texture.AsMemory(1)) is null)throw new InvalidDataException("Unresolved native texture");
        }
        File.WriteAllText(Path.Combine(output,low?"low-geometry-build.json":"geometry-build.json"),JsonSerializer.Serialize(new{SourceModel=high.Sha256,SourceLivery="00_official",NativeOutputSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputFile))),NativeRuntimeValidated=false,Materials=materialReport,Textures=textureReport,SelectedMeshes=selected,SuspensionBindings=suspensionReport,RestoredNonrenderAnchors=restoredAnchors,Limitations=new[]{"Exterior geometry; source cockpit uses a separate profile","Native distance transitions require runtime inspection","Blurred wheels omitted","Source arms, hubs and discs bound to native joints; endpoint/topology approximation needs runtime validation","Body damage retains donor attachments","Wheel binds neutralized; source static camber is not yet restored through native handling","Detail colour baked; native specular/emission mapping remains approximate","Handling and camera use separate profiles"}},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Native Ferrari exterior staged: {outputFile}");
    }
}
