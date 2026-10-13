using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

namespace DiRT2VR.CustomCars;

// Retarget the native character, not the car or shared character resources.
// AC KNH layout is documented by AcTools/KnhFile/KnhReader.cs (gro-ove/actools).
public static class FerrariDriver
{
    const string PoseHash="E4B310E8527E63EE4DCA261157F0D6403DBE9E34CD18DFF912BD5932D57089CE";
    const string ModelHash="CEDBC7B9DDACB1CFFC77ABDCF03AFF65823E89DB5B21D32419D7040ABDFAD1D5";
    sealed record Channel(string Bone,string Property,byte[] Bytes,int[] Values,bool Bezier,int NameOffset);
    static float Read(byte[] b,int o)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(o,4)));
    static void Put(byte[] b,int o,float v) { if(!float.IsFinite(v))throw new InvalidDataException("Nonfinite driver pose");BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(o,4),BitConverter.SingleToInt32Bits(v)); }
    static PssgFile Open(string path) { using var input=File.OpenRead(path);return PssgFile.Open(input); }
    static void Save(PssgFile file,string path) {using var output=File.Create(path);file.WritePssg(output);}
    static IEnumerable<Channel> Channels(PssgFile file,string? packet=null)
    {
        foreach(var data in file.Elements<PssgElement>().Where(n=>n.Name is "NeAnimPacketData_L4" or "NeAnimPacketData_S4" or "NeAnimPacketData_B1"))
        {
            if(packet is not null && data.ParentElement!.GetAttributeValue<string>("id")!=packet)continue;
            var b=data.Value;int p=0;
            int Int() {if(p+4>b.Length)throw new InvalidDataException("Truncated native animation");int v=BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p,4));p+=4;return v;}
            string Text() {int n=Int();if(n is <1 or >256 || p+n>b.Length)throw new InvalidDataException("Invalid animation name");string s=Encoding.UTF8.GetString(b,p,n);p+=n;return s;}
            int count=Int();if(count is <1 or >256 || Int()!=16)throw new InvalidDataException("Unsupported native animation packet");
            bool bezier=data.Name.EndsWith("B1",StringComparison.Ordinal);
            for(int i=0;i<count;i++)
            {
                int nameOffset=p;string bone=Text(),property=Text();int keys=Int();Int();Int();
                if(keys is <1 or >4096 || p+keys*(bezier?24:20)>b.Length)throw new InvalidDataException("Invalid native animation keys");
                var offsets=new int[keys];float previous=-1;
                for(int k=0;k<keys;k++)
                {
                    float time=Read(b,p);if(!float.IsFinite(time) || time<previous)throw new InvalidDataException("Invalid driver key time");previous=time;
                    offsets[k]=p+4;p+=bezier?24:20;
                }
                yield return new(bone,property,b,offsets,bezier,nameOffset);
            }
            if(p!=b.Length)throw new InvalidDataException("Unexpected native animation tail");
        }
    }
    static Dictionary<string,Matrix4x4> SourcePose(string path)
    {
        using var input=File.OpenRead(path);using var r=new BinaryReader(input,Encoding.UTF8);
        var result=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);int nodes=0;
        void Node(Matrix4x4 parent,int depth)
        {
            if(depth>64 || ++nodes>1024)throw new InvalidDataException("Driver hierarchy too deep");
            int n=r.ReadInt32();if(n is <1 or >256)throw new InvalidDataException("Invalid driver node name");string name=Encoding.UTF8.GetString(r.ReadBytes(n));
            var f=new float[16];for(int i=0;i<16;i++){f[i]=r.ReadSingle();if(!float.IsFinite(f[i]))throw new InvalidDataException("Nonfinite driver transform");}
            var m=new Matrix4x4(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15])*parent;
            if(!Matrix4x4.Invert(m,out _))throw new InvalidDataException("Singular driver transform");
            result.TryAdd(name,m);int children=r.ReadInt32();if(children is <0 or >256)throw new InvalidDataException("Invalid driver children");
            for(int i=0;i<children;i++)Node(m,depth+1);
        }
        Node(Matrix4x4.Identity,0);if(input.Position!=input.Length)throw new InvalidDataException("Driver pose trailing data");return result;
    }
    static Matrix4x4 Rotation(Matrix4x4 m) {m.Translation=Vector3.Zero;return m;}
    static Matrix4x4 Swing(Vector3 from,Vector3 to)
    {
        from=Vector3.Normalize(from);to=Vector3.Normalize(to);float dot=Vector3.Dot(from,to);
        if(dot<-.9999f)throw new InvalidDataException("Ambiguous driver bone reversal");
        var q=Quaternion.Normalize(new Quaternion(Vector3.Cross(from,to),1+dot));return Matrix4x4.CreateFromQuaternion(q);
    }
    static void WritePose(Channel c,Matrix4x4 pose)
    {
        if(c.Bezier)
        {
            if(c.Property!="translation" || c.Bone.Length<4 || c.Bone[^1]!=']')throw new InvalidDataException("Unsupported scalar driver channel");
            float v=c.Bone[^2] switch{'x'=>pose.M41,'y'=>pose.M42,'z'=>pose.M43,_=>throw new InvalidDataException("Unsupported driver axis")};
            foreach(int o in c.Values){Put(c.Bytes,o,v);Put(c.Bytes,o+4,v);Put(c.Bytes,o+12,v);}return;
        }
        Vector4 value;
        if(c.Property=="translation")value=new(pose.Translation,0);
        else if(c.Property=="rotation_quaternion") {var q=Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Rotation(pose)));value=new(q.X,q.Y,q.Z,q.W);}
        else throw new InvalidDataException("Unsupported driver channel: "+c.Property);
        foreach(int o in c.Values){Put(c.Bytes,o,value.X);Put(c.Bytes,o+4,value.Y);Put(c.Bytes,o+8,value.Z);Put(c.Bytes,o+12,value.W);}
    }
    static string Bone(Channel c)
    {
        string name=c.Bezier?c.Bone[..^3]:c.Bone;
        // Animation clips omit the actor prefix on the two shoulder-belt bones.
        return name is "tl_belt_Dummy" or "tr_belt_Dummy"?"d_"+name:name;
    }
    static void BindHarnessChannels(PssgFile animation)
    {
        // The stock clips address tl/tr_belt_Dummy, but the native skin names
        // these joints d_tl/d_tr_belt_Dummy. Updating values without the binding
        // leaves the mesh endpoints in the upright donor pose at runtime.
        var channels=Channels(animation).ToArray();
        foreach(var data in animation.Elements<PssgElement>().Where(e=>e.Name is "NeAnimPacketData_L4" or "NeAnimPacketData_S4" or "NeAnimPacketData_B1"))
        {
            var belts=channels.Where(c=>ReferenceEquals(c.Bytes,data.Value) && (c.Bone.StartsWith("tl_belt_Dummy",StringComparison.Ordinal) || c.Bone.StartsWith("tr_belt_Dummy",StringComparison.Ordinal))).ToArray();
            if(belts.Length==0)continue;
            using var output=new MemoryStream();int cursor=0;
            foreach(var c in belts)
            {
                output.Write(data.Value.AsSpan(cursor,c.NameOffset-cursor));byte[] name=Encoding.UTF8.GetBytes("d_"+c.Bone);
                var length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,name.Length);output.Write(length);output.Write(name);
                cursor=c.NameOffset+4+Encoding.UTF8.GetByteCount(c.Bone);
            }
            output.Write(data.Value.AsSpan(cursor));data.Value=output.ToArray();
            // Packet sizes/offsets describe the runtime channel/key arrays, not
            // serialized string bytes. Channel counts and key storage stay fixed.
        }
    }
    public static void Build(string source,string donor,string cockpit,string output)
    {
        source=Path.GetFullPath(source);donor=Path.GetFullPath(donor);cockpit=Path.GetFullPath(cockpit);output=Path.GetFullPath(output);
        foreach(string path in new[]{source,donor,cockpit,output})SafeFiles.NoLinks(path);
        if(Directory.Exists(output) || File.Exists(output) || new[]{source,donor,cockpit}.Any(p=>output.Equals(p,StringComparison.OrdinalIgnoreCase) || output.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Choose a new driver output outside the input folders");
        string sourcePose=Path.Combine(source,"driver_base_pos.knh"),nativeModel=Path.Combine(donor,"characters/models/driver_model.pssg");
        void Pins() {if(SafeFiles.Hash(sourcePose)!=PoseHash || SafeFiles.Hash(nativeModel)!=ModelHash)throw new IOException("Unsupported F2004 driver pose or native driver model");}
        Pins();var ac=SourcePose(sourcePose);var model=Open(nativeModel);var animation=Open(Path.Combine(donor,"interiors/prc/int_prc_driver_anims.pssg"));
        var joints=model.Elements<PssgJointNode>().ToArray();var local=joints.ToDictionary(n=>n.Id,n=>n.Transform.Transform,StringComparer.Ordinal);
        foreach(string packet in new[]{"packet6","packet7","packet8"})foreach(var c in Channels(animation,packet))
        {
            string bone=Bone(c);if(!local.TryGetValue(bone,out var m))continue;int o=c.Values[0];
            if(c.Bezier){var t=m.Translation;float v=Read(c.Bytes,o);switch(c.Bone[^2]){case 'x':t.X=v;break;case 'y':t.Y=v;break;case 'z':t.Z=v;break;}m.Translation=t;}
            else if(c.Property=="translation")m.Translation=new(Read(c.Bytes,o),Read(c.Bytes,o+4),Read(c.Bytes,o+8));
            else {var t=m.Translation;m=Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new(Read(c.Bytes,o),Read(c.Bytes,o+4),Read(c.Bytes,o+8),Read(c.Bytes,o+12))));m.Translation=t;}
            local[bone]=m;
        }
        var original=new Dictionary<string,Matrix4x4>();foreach(var n in joints)original[n.Id]=local[n.Id]*(n.ParentElement is PssgJointNode p?original[p.Id]:Matrix4x4.Identity);
        Vector3 At(string n)=>ac["DRIVER:"+n].Translation;
        var target=new Dictionary<string,Vector3>{{"Bip01",At("RIG_Center")},{"Bip01 Pelvis",At("RIG_Center")},{"Bip01 Spine",At("RIG_Hips")},{"Bip01 Spine1",Vector3.Lerp(At("RIG_Hips"),At("RIG_Cest"),.5f)},{"Bip01 Spine2",At("RIG_Cest")},{"Bip01 Neck",At("RIG_Nek")},{"Bip01 Head",At("RIG_Head")}};
        // Keep the harness ends behind the shoulders, below the helmet. The donor
        // anchors sit over 30 cm higher and become two stretched diagonal strips.
        target["d_tl_belt_Dummy"]=new(.095f,.445f,-.27f);
        target["d_tr_belt_Dummy"]=new(-.095f,.445f,-.27f);
        var child=new Dictionary<string,string>{{"Bip01 Pelvis","Bip01 Spine"},{"Bip01 Spine","Bip01 Spine1"},{"Bip01 Spine1","Bip01 Spine2"},{"Bip01 Spine2","Bip01 Neck"},{"Bip01 Neck","Bip01 Head"}};
        foreach(string side in new[]{"L","R"})
        {
            foreach(var pair in new[]{("Clavicle","Clave"),("UpperArm","Arm"),("Forearm","ForeArm"),("Hand","HAND"),("Thigh","Leg"),("Calf","Shin"),("Foot","Hill"),("Toe0","Foot")})target[$"Bip01 {side} {pair.Item1}"]=At($"RIG_{pair.Item2}_{side}");
            foreach(var pair in new[]{("Clavicle","UpperArm"),("UpperArm","Forearm"),("Forearm","Hand"),("Thigh","Calf"),("Calf","Foot"),("Foot","Toe0")})child[$"Bip01 {side} {pair.Item1}"]=$"Bip01 {side} {pair.Item2}";
        }
        var world=new Dictionary<string,Matrix4x4>();var fitted=new Dictionary<string,Matrix4x4>();
        foreach(var n in joints)
        {
            var parent=n.ParentElement is PssgJointNode p?world[p.Id]:Matrix4x4.Identity;
            var m=local[n.Id]*parent;
            if(target.TryGetValue(n.Id,out var position))
            {
                m=Rotation(original[n.Id]);
                if(child.TryGetValue(n.Id,out string? next))m*=Swing(original[next].Translation-original[n.Id].Translation,target[next]-position);
                m.Translation=position;
            }
            world[n.Id]=m;if(!Matrix4x4.Invert(parent,out var inverse))throw new InvalidDataException("Invalid native driver parent");fitted[n.Id]=m*inverse;
        }
        foreach(var c in Channels(animation))if(fitted.TryGetValue(Bone(c),out var pose))WritePose(c,pose);
        BindHarnessChannels(animation);
        var anchors=Open(Path.Combine(cockpit,"f04_anim_dummies.pssg"));
        var steer=anchors.TryGetObject<PssgNode>("steer_master".AsMemory())!.Transform.Transform;
        if(!Matrix4x4.Invert(steer,out var inverseSteer))throw new InvalidDataException("Invalid steering transform");
        var targets=new Dictionary<string,Matrix4x4>{{"d_bip_position",Matrix4x4.Identity},{"D_Lhand_Dummy",world["Bip01 L Hand"]*inverseSteer},{"D_Rhand_Dummy",world["Bip01 R Hand"]*inverseSteer},{"D_Lfoot_Dummy",world["Bip01 L Foot"]},{"D_Rfoot_Dummy",world["Bip01 R Foot"]},{"D_brake_Dummy",world["Bip01 L Foot"]},{"D_clutch_Dummy",world["Bip01 L Foot"]},{"D_accelerator_Dummy",world["Bip01 R Foot"]}};
        // Native shift/handbrake IK still consumes these targets even when the
        // blend setting is zero. Keep both reach states at the right-hand grip.
        targets["sequential_gear_Dummy01"]=world["Bip01 R Hand"];
        targets["handbrake_Dummy01"]=world["Bip01 R Hand"];
        targets["LHD_SEQ_GearStick_Dummy"]=Matrix4x4.Identity;
        targets["LHD_HandBrake_Dummy"]=Matrix4x4.Identity;
        foreach(string name in new[]{"Gear_pos_01","Gear_pos_02","Gear_pos_03"})targets[name]=world["Bip01 R Hand"];
        foreach(var node in anchors.Elements<PssgNode>())if(targets.TryGetValue(node.Id,out var pose))node.Transform.Transform=pose;
        var interior=Open(Path.Combine(cockpit,"int_f04.pssg"));foreach(var node in interior.Elements<PssgNode>())if(targets.TryGetValue(node.Id,out var pose))node.Transform.Transform=pose;
        var controls=Open(Path.Combine(donor,"interiors/prc/int_prc_anims.pssg"));
        foreach(var c in Channels(controls))if(targets.TryGetValue(Bone(c),out var pose))WritePose(c,pose);
        // The F2004 uses paddles. Avoid reaching for the donor's gear lever or handbrake.
        using var configInput=File.OpenRead(Path.Combine(donor,"interiors/prc/int_prc_anim.xml"));var config=new EgoEngineLibrary.Xml.XmlFile(configInput);
        foreach(System.Xml.XmlElement e in config.Document.SelectNodes("//blend_values/blend")!)e.SetAttribute("value","0");
        using var cameraInput=File.OpenRead(Path.Combine(cockpit,"cameras.xml"));var camera=new EgoEngineLibrary.Xml.XmlFile(cameraInput);
        foreach(System.Xml.XmlElement e in camera.Document.SelectNodes("//Parameter[@name='DisableDriver']")!)e.SetAttribute("value","false");
        // Head remains hidden only from the player's cockpit; the native chase path shows it.
        Directory.CreateDirectory(output);Save(animation,Path.Combine(output,"int_f04_driver_anims.pssg"));Save(controls,Path.Combine(output,"int_f04_anims.pssg"));Save(anchors,Path.Combine(output,"f04_anim_dummies.pssg"));Save(interior,Path.Combine(output,"int_f04.pssg"));
        using(var f=File.Create(Path.Combine(output,"int_f04_anim.xml")))config.Write(f,config.Type);
        using(var f=File.Create(Path.Combine(output,"cameras.xml")))camera.Write(f,camera.Type);
        Pins();SafeFiles.WriteJson(Path.Combine(output,"driver-build.json"),new{PoseHash,ModelHash,JointCount=joints.Length,NativeRuntimeValidated=false,NativeDriverMeshUnchanged=true,Joints=world.Select(p=>new{Name=p.Key,Position=new[]{p.Value.M41,p.Value.M42,p.Value.M43}}),Files=Directory.GetFiles(output).Select(f=>new{Path=Path.GetFileName(f),Sha256=SafeFiles.Hash(f)})});
    }
}
