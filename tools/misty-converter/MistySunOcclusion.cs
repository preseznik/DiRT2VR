using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Dds;

// Static, world-space visibility for Misty's three fixed suns. Native shadows
// still handle nearby moving objects; their distance fade cannot relight hills.
internal static class MistySunOcclusion
{
    internal const string TextureName="misty_road_sun_visibility.dds";
    internal static readonly Vector3[] Directions=[Sun(22,1),Sun(12,1),Sun(10,-1)];
    static Vector3 Sun(float elevation,float x)=>new(x*MathF.Cos(elevation*MathF.PI/180),MathF.Sin(elevation*MathF.PI/180),0);
    internal static void Build(Scene scene,string output)
    {
        const float cell=2;
        var road=scene.Visuals.Where(m=>m.Material.Name=="road_asphalt").ToArray();
        var points=road.SelectMany(m=>m.Positions).ToArray();
        var min=points.Aggregate(Vector3.Min)-new Vector3(8,0,8);
        var max=points.Aggregate(Vector3.Max)+new Vector3(8,0,8);
        int Size(float span){int n=1;while(n*cell<span)n*=2;if(n>4096)throw new InvalidDataException("Misty sun mask exceeds bounded size");return n;}
        int width=Size(max.X-min.X),height=Size(max.Z-min.Z);
        var heights=Enumerable.Repeat(float.PositiveInfinity,width*height).ToArray();
        foreach(var mesh in road)for(int t=0;t<mesh.Indices.Length;t+=3)
        {
            var a=mesh.Positions[mesh.Indices[t]];var b=mesh.Positions[mesh.Indices[t+1]];var c=mesh.Positions[mesh.Indices[t+2]];
            float d=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);if(MathF.Abs(d)<1e-8f)continue;
            var lo=Vector3.Min(a,Vector3.Min(b,c));var hi=Vector3.Max(a,Vector3.Max(b,c));
            for(int z=Math.Max(0,(int)((lo.Z-min.Z)/cell));z<=Math.Min(height-1,(int)((hi.Z-min.Z)/cell));z++)
            for(int x=Math.Max(0,(int)((lo.X-min.X)/cell));x<=Math.Min(width-1,(int)((hi.X-min.X)/cell));x++)
            {
                float px=min.X+(x+.5f)*cell,pz=min.Z+(z+.5f)*cell;
                float u=((b.Z-c.Z)*(px-c.X)+(c.X-b.X)*(pz-c.Z))/d;
                float v=((c.Z-a.Z)*(px-c.X)+(a.X-c.X)*(pz-c.Z))/d;
                if(u>=0&&v>=0&&u+v<=1)heights[z*width+x]=Math.Min(heights[z*width+x],u*a.Y+v*b.Y+(1-u-v)*c.Y);
            }
        }
        var blockers=scene.Visuals.Where(m=>m.Active&&Visuals.IsOpaque(m.Material)&&!m.Material.AlphaTest&&
            m.Material.Name is not ("road_asphalt" or "lake1")&&!Visuals.Target(m.Material).Contains("animated",StringComparison.Ordinal));
        var tree=new Occluders(blockers.SelectMany(m=>Enumerable.Range(0,m.Indices.Length/3).Select(t=>
            new Triangle(m.Positions[m.Indices[t*3]],m.Positions[m.Indices[t*3+1]],m.Positions[m.Indices[t*3+2]]))).ToArray());
        var raw=new byte[width*height*4];
        Parallel.For(0,height,z=>{
            for(int x=0;x<width;x++)
            {
                float y=heights[z*width+x];if(!float.IsFinite(y))continue;
                var p=new Vector3(min.X+(x+.5f)*cell,y+.12f,min.Z+(z+.5f)*cell);
                for(int channel=0;channel<Directions.Length;channel++)
                {
                    bool clear=true;
                    // Sample the cell footprint, then erode visibility below. This
                    // keeps filtering from leaking sun across mountain boundaries.
                    for(int dz=-1;dz<=1&&clear;dz++)for(int dx=-1;dx<=1&&clear;dx++)for(int dy=-1;dy<=1&&clear;dy++)
                        clear=!tree.Blocked(p+new Vector3(dx*cell*.5f,dy*.5f,dz*cell*.5f),Directions[channel]);
                    raw[(z*width+x)*4+channel]=clear?(byte)255:(byte)0;
                }
            }
        });
        var pixels=new byte[raw.Length];
        for(int z=1;z<height-1;z++)for(int x=1;x<width-1;x++)for(int channel=0;channel<3;channel++)
        {
            byte visible=255;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)visible=Math.Min(visible,raw[((z+dz)*width+x+dx)*4+channel]);
            pixels[(z*width+x)*4+channel]=visible;
        }
        // Minimum-reduced mip levels preserve occlusion at every viewing distance.
        // Uncompressed channels avoid BC colour fitting inventing a lit texel.
        using var mipData=new MemoryStream();var level=pixels;int w=width,h=height,levels=0;
        while(true)
        {
            mipData.Write(level);levels++;if(w==1&&h==1)break;
            int nw=Math.Max(1,w/2),nh=Math.Max(1,h/2);var next=new byte[nw*nh*4];
            for(int z=0;z<nh;z++)for(int x=0;x<nw;x++)for(int channel=0;channel<3;channel++)
            {
                byte value=255;
                for(int dz=0;dz<2;dz++)for(int dx=0;dx<2;dx++)value=Math.Min(value,level[(Math.Min(h-1,z*2+dz)*w+Math.Min(w-1,x*2+dx))*4+channel]);
                next[(z*nw+x)*4+channel]=value;
            }
            level=next;w=nw;h=nh;
        }
        var dds=new DdsFile();dds.header.width=(uint)width;dds.header.height=(uint)height;dds.header.mipMapCount=(uint)levels;
        dds.header.ddspf.flags=DdsPixelFormat.Flags.DDPF_RGB|DdsPixelFormat.Flags.DDPF_ALPHAPIXELS;
        dds.header.ddspf.rGBBitCount=32;dds.header.ddspf.rBitMask=0xff0000;dds.header.ddspf.gBitMask=0xff00;dds.header.ddspf.bBitMask=0xff;dds.header.ddspf.aBitMask=0xff000000;
        dds.header.flags|=DdsHeader.Flags.DDSD_MIPMAPCOUNT|DdsHeader.Flags.DDSD_PITCH;
        dds.header.pitchOrLinearSize=(uint)(width*4);dds.header.caps|=DdsHeader.Caps.DDSCAPS_MIPMAP|DdsHeader.Caps.DDSCAPS_COMPLEX;
        dds.bdata=mipData.ToArray();
        // EGO ui8x4 uses BGRA bytes and does not retain DDS channel masks.
        for(int i=0;i<dds.bdata.Length;i+=4)(dds.bdata[i],dds.bdata[i+2])=(dds.bdata[i+2],dds.bdata[i]);
        using var encoded=new MemoryStream();dds.Write(encoded);var bytes=encoded.ToArray();
        scene.Textures[TextureName]=new(bytes,MistyTexturePacking.ConvertHash(bytes));
        string mapping=string.Join(" ",new[]{1/(width*cell),1/(height*cell),-min.X/(width*cell),-min.Z/(height*cell)}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        foreach(var material in road.Select(m=>m.Material).Distinct())
        {
            var native=material.Native!;var definitions=native.Group.Elements("SHADERINPUTDEFINITION").ToArray();
            int index=Array.FindIndex(definitions,d=>(string?)d.Attribute("name")=="MistySunTransform");
            native.Instance.Elements("SHADERINPUT").Single(i=>(int?)i.Attribute("parameterID")==index).Value=mapping;
        }
        Files.Json(Path.Combine(output,"sun-occlusion.json"),new{Width=width,Height=height,CellMetres=cell,MipLevels=levels,
            RoadCells=heights.Count(float.IsFinite),BlockerTriangles=tree.Count,Directions=Directions.Select(d=>new[]{d.X,d.Y,d.Z}),
            VisibleCells=Enumerable.Range(0,3).Select(c=>Enumerable.Range(0,width*height).Count(i=>pixels[i*4+c]==255)),
            Mapping=mapping,ConservativeFootprintSamples=27,VisibilityErosionCells=1,MinimumMips=true,
            Gates=scene.Gates.Select(g=>new{g.Distance,Position=new[]{g.Position.X,g.Position.Y,g.Position.Z},
                Blocked=Directions.Select(d=>tree.Blocked(g.Position+Vector3.UnitY*.12f,d)).ToArray()})});
        File.WriteAllBytes(Path.Combine(output,TextureName),bytes);
    }
    internal readonly record struct Triangle(Vector3 A,Vector3 B,Vector3 C)
    {
        internal Bounds Bounds=>new(Vector3.Min(A,Vector3.Min(B,C)),Vector3.Max(A,Vector3.Max(B,C)));
        internal Vector3 Center=>(A+B+C)/3;
    }
    internal sealed class Occluders
    {
        readonly Triangle[] triangles;readonly List<Node> nodes=[];
        readonly record struct Node(Bounds Bounds,int Start,int Count,int Left,int Right);
        internal int Count=>triangles.Length;
        internal Occluders(Triangle[] values){triangles=values;if(values.Length==0)throw new InvalidDataException("Missing sun occluders");Build(0,values.Length);}
        int Build(int start,int count)
        {
            var bounds=triangles[start].Bounds;for(int i=start+1;i<start+count;i++)bounds=bounds.Union(triangles[i].Bounds);
            int id=nodes.Count;nodes.Add(default);
            if(count<=12){nodes[id]=new(bounds,start,count,-1,-1);return id;}
            var span=bounds.Max-bounds.Min;int axis=span.X>=span.Y&&span.X>=span.Z?0:span.Y>=span.Z?1:2;
            Array.Sort(triangles,start,count,Comparer<Triangle>.Create((a,b)=>a.Center[axis].CompareTo(b.Center[axis])));
            int half=count/2,left=Build(start,half),right=Build(start+half,count-half);nodes[id]=new(bounds,0,0,left,right);return id;
        }
        internal bool Blocked(Vector3 origin,Vector3 direction)=>Visit(0,origin,direction);
        bool Visit(int id,Vector3 p,Vector3 d)
        {
            var node=nodes[id];float near=.01f,far=float.PositiveInfinity;
            for(int axis=0;axis<3;axis++)
            {
                if(MathF.Abs(d[axis])<1e-8f){if(p[axis]<node.Bounds.Min[axis]||p[axis]>node.Bounds.Max[axis])return false;continue;}
                float a=(node.Bounds.Min[axis]-p[axis])/d[axis],b=(node.Bounds.Max[axis]-p[axis])/d[axis];
                near=Math.Max(near,Math.Min(a,b));far=Math.Min(far,Math.Max(a,b));if(near>far)return false;
            }
            if(node.Count==0)return Visit(node.Left,p,d)||Visit(node.Right,p,d);
            for(int i=node.Start;i<node.Start+node.Count;i++)
            {
                var t=triangles[i];var e1=t.B-t.A;var e2=t.C-t.A;var cross=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,cross);
                if(MathF.Abs(det)<1e-8f)continue;
                var delta=p-t.A;float u=Vector3.Dot(delta,cross)/det;if(u<0||u>1)continue;
                var q=Vector3.Cross(delta,e1);float v=Vector3.Dot(d,q)/det;if(v<0||u+v>1)continue;
                if(Vector3.Dot(e2,q)/det>.01f)return true;
            }
            return false;
        }
    }
}
