using DiRT2VR.CarImport;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Scenes;

namespace DiRT2VR.CustomCars;

internal static class FerrariInterior
{
    sealed record Part(CarMesh Mesh,Matrix4x4 World,bool Steering,bool Body);
    internal static void Build(string source,string donor,string output)
    {
        source=Path.GetFullPath(source);output=Path.GetFullPath(output);
        if(Directory.Exists(output) || output==source || output.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("New output outside source required");
        var model=CarKn5.Read(Path.Combine(source,"ferrari_f2004.kn5"));
        using var input=File.OpenRead(Path.Combine(donor,"interiors/prc/int_prc.pssg"));var pssg=PssgFile.Open(input);
        if(!GltfCarInteriorPssgConverter.SupportsPssg(pssg))throw new InvalidDataException("Unsupported donor interior hierarchy");
        var originalNodes=pssg.Elements<PssgNode>().ToDictionary(n=>n.Id);
        var parts=new List<Part>();Matrix4x4 steeringPivot=default;var selected=new List<object>();
        void Visit(CarNode node,Matrix4x4 world,bool enabled,bool cockpit,bool steering,bool exteriorExcluded)
        {
            world=node.LocalTransform*world;enabled&=node.Active;cockpit|=node.Name=="COCKPIT_HR";
            // Inactive belt/driver alternatives must also be excluded inside HR cockpit.
            if(node.Name is "CINTURE_OFF" or "g_Custom_Driver")return;
            if(node.Name=="STEER_HR"){steering=true;steeringPivot=world;}
            // A native cockpit owns the surrounding rigid bodywork as well as
            // its HR controls. Wheels and linkages stay in the exterior rig.
            exteriorExcluded|=node.Name is "COCKPIT_HR" or "COCKPIT_LR" or "CINTURE_OFF" or "g_Custom_Driver"
                || node.Name.StartsWith("WHEEL_",StringComparison.Ordinal) || FerrariSuspension.Joint(node.Name) is not null
                || node.Name.Contains("Blur",StringComparison.OrdinalIgnoreCase) || node.Name.Contains("Damage_Glass",StringComparison.OrdinalIgnoreCase);
            if(enabled && (cockpit || !exteriorExcluded) && node.Mesh is {} mesh && mesh.Visible && mesh.Renderable)
            {
                bool body=!cockpit;
                parts.Add(new(mesh,world,steering,body));selected.Add(new{node.Name,Steering=steering,Assembly=steering?"int_steering_wheel":"int_main",Role=body?"bodywork":"cockpit",Triangles=mesh.Indices.Length/3});
            }
            foreach(var child in node.Children)Visit(child,world,enabled,cockpit,steering,exteriorExcluded);
        }
        Visit(model.Root,Matrix4x4.Identity,true,false,false,false);
        if(!parts.Any(p=>p.Steering) || !parts.Any(p=>!p.Steering) || !Matrix4x4.Invert(steeringPivot,out var inverseSteering))throw new InvalidDataException("Missing rigid cockpit/steering assembly");
        var materialSet=FerrariMaterials.Build(model,source,pssg,pssg.Elements<PssgTexture>().First(),true,parts.Select(p=>p.Mesh.Material).ToHashSet());
        var root=new NodeBuilder("Scene Root");var scene=new SceneBuilder();
        var master=root.CreateNode("steer_master");master.LocalTransform=steeringPivot;
        var wheel=master.CreateNode("steering_wheel");
        // Keep all non-steering cockpit/bodywork geometry in one native render
        // node: one transform, one pose owner. Separate rigid render roots allow
        // the engine's cockpit pose path to treat surrounding bodywork differently.
        foreach(var group in parts.GroupBy(p=>p.Steering))
        {
            string name=group.Key?"int_steering_wheel":"int_main";
            var node=(group.Key?wheel:root).CreateNode(name);
            var mesh=new MeshBuilder<VertexPositionNormalTangent,VertexColor1Texture4,VertexEmpty>(name);
            foreach(var part in group)
            {
                var transform=part.World*(group.Key?inverseSteering:Matrix4x4.Identity);
                if(!Matrix4x4.Invert(transform,out var inverse))throw new InvalidDataException("Singular cockpit transform");
                var normals=Matrix4x4.Transpose(inverse);var primitive=mesh.UsePrimitive(materialSet.Materials[part.Mesh.Material]);
                VertexBuilder<VertexPositionNormalTangent,VertexColor1Texture4,VertexEmpty> Vertex(int i)
                {
                    var normal=Vector3.TransformNormal(part.Mesh.Normals[i],normals);normal=normal.LengthSquared()>1e-10f?Vector3.Normalize(normal):Vector3.UnitY;
                    var tangent=Vector3.TransformNormal(part.Mesh.Tangents[i],transform);tangent-=normal*Vector3.Dot(normal,tangent);
                    if(tangent.LengthSquared()<1e-10f)tangent=Vector3.Cross(normal,Math.Abs(normal.Y)<.9f?Vector3.UnitY:Vector3.UnitX);
                    var v=new VertexBuilder<VertexPositionNormalTangent,VertexColor1Texture4,VertexEmpty>();v.Geometry.Position=Vector3.Transform(part.Mesh.Positions[i],transform);
                    v.Geometry.Normal=normal;v.Geometry.Tangent=new(Vector3.Normalize(tangent),1);v.Material.Color=Vector4.One;
                    v.Material.TexCoord0=v.Material.TexCoord1=v.Material.TexCoord2=v.Material.TexCoord3=part.Mesh.UV[i];return v;
                }
                for(int i=0;i<part.Mesh.Indices.Length;i+=3)primitive.AddTriangle(Vertex(part.Mesh.Indices[i]),Vertex(part.Mesh.Indices[i+1]),Vertex(part.Mesh.Indices[i+2]));
            }
            scene.AddRigidMesh(mesh,node);
        }
        new GltfCarInteriorPssgConverter().Convert(scene.ToGltf2(),pssg);
        // Keep named non-render animation anchors; old PRC render geometry is removed.
        foreach(var original in originalNodes.Values.Where(n=>n.GetType()==typeof(PssgNode)))
        {
            if(pssg.TryGetObject<PssgNode>(original.Id.AsMemory()) is not null)continue;
            var parentId=(original.ParentElement as PssgObject)?.Id??throw new InvalidDataException("Missing anchor parent");
            var parent=pssg.TryGetObject<PssgNode>(parentId.AsMemory())??throw new InvalidDataException("Unresolved anchor parent: "+parentId);
            var clone=(PssgNode)original.DeepClone();
            foreach(var child in clone.ChildElements.OfType<PssgNode>().ToArray())clone.RemoveChild(child);
            parent.AppendChild(clone);
        }
        FerrariLayout.AdjustAnchors(pssg);
        FerrariDashboard.Add(pssg, donor);
        Directory.CreateDirectory(output);var path=Path.Combine(output,"int_f04.pssg");using(var target=File.Create(path))pssg.WritePssg(target);
        using(var verify=File.OpenRead(path))
        {
            var readback=PssgFile.Open(verify);
            if(!GltfCarInteriorPssgConverter.SupportsPssg(readback) || readback.TryGetObject<PssgRenderNode>("int_steering_wheel".AsMemory()) is null)throw new InvalidDataException("Cockpit readback failed");
        }
        FerrariLayout.Build(source,donor,output,steeringPivot);
        var files=Directory.GetFiles(output).Select(f=>new{Path=Path.GetFileName(f),Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}).ToArray();
        File.WriteAllText(Path.Combine(output,"cockpit-build.json"),JsonSerializer.Serialize(new{SourceModel=model.Sha256,Files=files,SelectedMeshes=selected,Materials=materialSet.MaterialReport,Textures=materialSet.TextureReport,NativeRuntimeValidated=false,Limitations=new[]{"Native driver fitting is applied by the separate driver conversion pass","Source detail colour baked; donor reflection/specular approximation","Native five-digit RPM readout; remaining AC display/LED telemetry not mapped","Mirrors remain donor approximation","Bodywork and HR cockpit use the separate native interior; camera comfort remains unaccepted","Donor handling remains"}},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Native Ferrari cockpit staged: {path}");
    }
}
