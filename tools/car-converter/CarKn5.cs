using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace DiRT2VR.CarImport;

internal sealed record CarMaterial(string Name, string Shader, byte Blend, bool AlphaTest, uint DepthMode,
    Dictionary<string,float[]> Properties, Dictionary<string,string> Textures);
internal sealed record CarTexture(string Name, int Active, byte[] Data, string Sha256);
internal sealed record CarMesh(bool CastShadows, bool Visible, bool Transparent, Vector3[] Positions,
    Vector3[] Normals, Vector2[] UV, Vector3[] Tangents, ushort[] Indices, int Material,
    uint Layer, float LodIn, float LodOut, float[] Bounds, bool Renderable);
internal sealed record CarNode(int Kind, string Name, bool Active, Matrix4x4 LocalTransform,
    CarMesh? Mesh, CarNode[] Children);
internal sealed record CarModel(string Source, string Sha256, int Version, int? Extra,
    CarTexture[] Textures, CarMaterial[] Materials, CarNode Root);

// Vehicle meshes stay in their original local spaces. Animated wheel and steering
// transforms are retained, unlike the track reader's intentional world-space bake.
internal static class CarKn5
{
    internal static CarModel Read(string path)
    {
        using var stream=File.OpenRead(path);
        using var r=new BinaryReader(stream,Encoding.UTF8);
        byte[] Bytes(int n)
        {
            if(n<0 || n>stream.Length-stream.Position)throw new InvalidDataException("Truncated KN5: "+path);
            var data=r.ReadBytes(n);if(data.Length!=n)throw new EndOfStreamException();return data;
        }
        int Count(int limit,string kind)
        {
            var n=r.ReadInt32();if(n<0 || n>limit)throw new InvalidDataException("Invalid "+kind+" count: "+n);return n;
        }
        string Text()=>new UTF8Encoding(false,true).GetString(Bytes(Count(65536,"string")));
        float Float(){var f=r.ReadSingle();if(!float.IsFinite(f))throw new InvalidDataException("Nonfinite KN5 value");return f;}
        Vector3 V3()=>new(Float(),Float(),Float());
        if(Encoding.ASCII.GetString(Bytes(6))!="sc6969")throw new InvalidDataException("Not KN5");
        int version=r.ReadInt32();if(version is not (5 or 6))throw new InvalidDataException("Unsupported KN5 version");
        int? extra=version==6?r.ReadInt32():null;
        var textures=new CarTexture[Count(4096,"texture")];
        for(int i=0;i<textures.Length;i++)
        {
            var active=r.ReadInt32();var name=Text();var data=Bytes(Count(64*1024*1024,"texture bytes"));
            textures[i]=new(name,active,data,Convert.ToHexString(SHA256.HashData(data)));
        }
        var materials=new CarMaterial[Count(4096,"material")];
        for(int i=0;i<materials.Length;i++)
        {
            var name=Text();var shader=Text();var blend=r.ReadByte();var alpha=r.ReadBoolean();var depth=r.ReadUInt32();
            var properties=new Dictionary<string,float[]>(StringComparer.Ordinal);
            int count=Count(4096,"property");for(int j=0;j<count;j++){var key=Text();var values=new float[10];for(int k=0;k<10;k++)values[k]=Float();properties.Add(key,values);}
            var mappings=new Dictionary<string,string>(StringComparer.Ordinal);
            count=Count(64,"texture mapping");for(int j=0;j<count;j++){var key=Text();r.ReadUInt32();mappings.Add(key,Text());}
            materials[i]=new(name,shader,blend,alpha,depth,properties,mappings);
        }
        int nodes=0;
        CarNode Node(int depth)
        {
            if(depth>64 || ++nodes>100000)throw new InvalidDataException("KN5 hierarchy exceeds limits");
            int kind=r.ReadInt32();string name=Text();int count=Count(100000,"child");bool active=r.ReadBoolean();
            var transform=Matrix4x4.Identity;CarMesh? mesh=null;
            if(kind==1)
            {
                var m=new float[16];for(int i=0;i<16;i++)m[i]=Float();
                transform=new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
                if(!Matrix4x4.Invert(transform,out _))throw new InvalidDataException("Singular node transform: "+name);
            }
            else if(kind==2)
            {
                bool shadow=r.ReadBoolean(),visible=r.ReadBoolean(),transparent=r.ReadBoolean();
                int vertices=Count(65536,"vertex");var p=new Vector3[vertices];var n=new Vector3[vertices];var uv=new Vector2[vertices];var tangent=new Vector3[vertices];
                for(int i=0;i<vertices;i++){p[i]=V3();n[i]=V3();uv[i]=new(Float(),Float());tangent[i]=V3();}
                int ni=Count(4000000,"index");if(ni%3!=0)throw new InvalidDataException("Nontriangular mesh: "+name);
                var indices=new ushort[ni];for(int i=0;i<ni;i++){indices[i]=r.ReadUInt16();if(indices[i]>=vertices)throw new InvalidDataException("Mesh index out of range: "+name);}
                if(materials.Length==0)throw new InvalidDataException("Mesh without materials");
                int material=Count(materials.Length-1,"material index");uint layer=r.ReadUInt32();float lodIn=Float(),lodOut=Float();
                var bounds=new float[4];for(int i=0;i<4;i++)bounds[i]=Float();bool renderable=r.ReadBoolean();
                mesh=new(shadow,visible,transparent,p,n,uv,tangent,indices,material,layer,lodIn,lodOut,bounds,renderable);
            }
            else throw new InvalidDataException("Unsupported car node class: "+kind);
            var children=new CarNode[count];for(int i=0;i<count;i++)children[i]=Node(depth+1);
            return new(kind,name,active,transform,mesh,children);
        }
        var root=Node(0);if(stream.Position!=stream.Length)throw new InvalidDataException("Unexpected KN5 trailing data");
        stream.Position=0;return new(Path.GetFullPath(path),Convert.ToHexString(SHA256.HashData(stream)),version,extra,textures,materials,root);
    }
}
