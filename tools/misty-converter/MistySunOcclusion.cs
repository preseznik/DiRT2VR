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
    internal static Occluders Build(Scene scene,string output,bool highQualityAssets=false)
    {
        const float cell=1;
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
        int roadCells=heights.Count(float.IsFinite);
        // Padding supplies filtering neighbours at road edges. An uncovered texel
        // is not an occluder: treating it as one cut square notches into the sheen.
        for(int pass=0;pass<3;pass++)
        {
            var next=(float[])heights.Clone();
            for(int z=1;z<height-1;z++)for(int x=1;x<width-1;x++)if(!float.IsFinite(heights[z*width+x]))
            {
                float sum=0;int count=0;
                foreach(int offset in new[]{-1,1,-width,width})if(float.IsFinite(heights[z*width+x+offset])){sum+=heights[z*width+x+offset];count++;}
                if(count>0)next[z*width+x]=sum/count;
            }
            heights=next;
        }
        float Height(float x,float z)
        {
            float px=(x-min.X)/cell-.5f,pz=(z-min.Z)/cell-.5f;int ix=(int)MathF.Floor(px),iz=(int)MathF.Floor(pz);
            float total=0,weight=0;
            for(int dz=0;dz<2;dz++)for(int dx=0;dx<2;dx++)
            {
                int xx=ix+dx,zz=iz+dz;if(xx<0||xx>=width||zz<0||zz>=height)continue;
                float y=heights[zz*width+xx],w=(dx==0?1-(px-ix):px-ix)*(dz==0?1-(pz-iz):pz-iz);
                if(float.IsFinite(y)){total+=y*w;weight+=w;}
            }
            return weight>0?total/weight:float.PositiveInfinity;
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
                float heightRange=0;
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                {
                    float sample=Height(p.X+dx*cell,p.Z+dz*cell);
                    if(float.IsFinite(sample))heightRange=Math.Max(heightRange,MathF.Abs(sample-y));
                }
                for(int channel=0;channel<Directions.Length;channel++)
                {
                    // Intersect the complete filtering footprint with projected
                    // caster triangles. Sparse rays can miss narrow walls/signs.
                    // Height variation follows the road, not a below-road bias.
                    bool clear=!tree.BlockedFootprint(p,Directions[channel],new Vector3(cell,heightRange,cell));
                    raw[(z*width+x)*4+channel]=clear?(byte)255:(byte)0;
                }
            }
        });
        var pixels=raw;
        // Keep a complete conservative mip chain for the texture container.
        // The shader samples level zero so visibility stays fixed in world space.
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
        Vector3 RoadPoint(Gate g)
        {
            float y=Height(g.Position.X,g.Position.Z);
            return new(g.Position.X,float.IsFinite(y)?y:g.Position.Y,g.Position.Z);
        }
        Files.Json(Path.Combine(output,"sun-occlusion.json"),new{Width=width,Height=height,CellMetres=cell,MipLevels=levels,
            RoadCells=roadCells,PaddedCells=heights.Count(float.IsFinite),BlockerTriangles=tree.Count,Directions=Directions.Select(d=>new[]{d.X,d.Y,d.Z}),
            VisibleCells=Enumerable.Range(0,3).Select(c=>Enumerable.Range(0,width*height).Count(i=>pixels[i*4+c]==255)),
            Mapping=mapping,ConservativeFootprintIntersection=true,VisibilityErosionCells=0,MinimumMips=true,FixedSampleLod=0,
            // AC's AI points sit roughly 0.4 m above the road. Audit sunlight at
            // the reflecting surface, retaining the AI point for traceability.
            Gates=scene.Gates.Select(g=>new{g.Distance,RoadSurface=float.IsFinite(Height(g.Position.X,g.Position.Z)),Position=new[]{RoadPoint(g).X,RoadPoint(g).Y,RoadPoint(g).Z},SourcePosition=new[]{g.Position.X,g.Position.Y,g.Position.Z},
                Blocked=Directions.Select(d=>tree.Blocked(RoadPoint(g)+Vector3.UnitY*.12f,d)).ToArray()})});
        File.WriteAllBytes(Path.Combine(output,TextureName),bytes);
        if(highQualityAssets)Objects(scene,tree,output);
        return tree;
    }
    internal static int SunBits(Occluders tree,Vector3 position,Vector3 normal)
    {
        if(normal.LengthSquared()<1e-8f)normal=Vector3.UnitY;else normal=Vector3.Normalize(normal);
        int bits=0;
        for(int c=0;c<Directions.Length;c++)if(!tree.Blocked(position+normal*.08f+Directions[c]*.02f,Directions[c]))bits|=1<<c;
        return bits;
    }
    // Cosine-weighted hemisphere samples for the small generated grass cards.
    // This is fixed sky visibility, not a sun-dependent darkening multiplier.
    static readonly Vector3[] SkyDirections=Enumerable.Range(0,16).Select(i=>{
        float radius=MathF.Sqrt((i+.5f)/16),angle=i*2.39996323f;
        return new Vector3(radius*MathF.Cos(angle),MathF.Sqrt(1-radius*radius),radius*MathF.Sin(angle));
    }).ToArray();
    internal static float SkyVisibility(Occluders tree,Vector3 position)
        =>SkyDirections.Count(d=>!tree.Blocked(position+Vector3.UnitY*.08f,d))/(float)SkyDirections.Length;
    static void Objects(Scene scene,Occluders tree,string output)
    {
        var report=new List<object>();var materials=new Dictionary<(string,int),Material>();
        for(int m=0;m<scene.Visuals.Count;m++)
        {
            var mesh=scene.Visuals[m];var native=mesh.Material.Native;
            if(native is null||!native.Group.Elements("SHADERINPUTDEFINITION").Any(d=>(string?)d.Attribute("name")=="MistyVisibility"))continue;
            if(Visuals.Target(mesh.Material)=="misty_object_rgb.fx")
            {
                if(mesh.Colors is null||mesh.Colors.Length!=mesh.Positions.Length)throw new InvalidDataException("Missing HQ ambient colours: "+mesh.Name);
                var bits=new int[mesh.Positions.Length];
                for(int v=0;v<bits.Length;v++)bits[v]=SunBits(tree,mesh.Positions[v],mesh.Normals[v]);
                // A blocked triangle centre must not be lit just because its
                // corners happen to straddle a narrow mountain shadow.
                for(int t=0;t<mesh.Indices.Length;t+=3)
                {
                    int a=mesh.Indices[t],b=mesh.Indices[t+1],c=mesh.Indices[t+2];
                    int centre=SunBits(tree,(mesh.Positions[a]+mesh.Positions[b]+mesh.Positions[c])/3,mesh.Normals[a]+mesh.Normals[b]+mesh.Normals[c]);
                    bits[a]&=centre;bits[b]&=centre;bits[c]&=centre;
                }
                mesh=mesh with{Colors=mesh.Colors.Select((v,i)=>new Vector4(v.X,v.Y,v.Z,bits[i]/255f)).ToArray()};
            }
            int mask=0;
            for(int channel=0;channel<Directions.Length;channel++)
            {
                var direction=Directions[channel];bool visible=true,receives=false;
                for(int t=0;t<mesh.Indices.Length&&visible;t+=3)
                {
                    int ia=mesh.Indices[t],ib=mesh.Indices[t+1],ic=mesh.Indices[t+2];
                    var a=mesh.Positions[ia];var b=mesh.Positions[ib];var c=mesh.Positions[ic];
                    var normal=mesh.Normals[ia]+mesh.Normals[ib]+mesh.Normals[ic];
                    if(normal.LengthSquared()<1e-8f||Vector3.Dot(normal,direction)<=0)continue;
                    receives=true;normal=Vector3.Normalize(normal);
                    // A whole source mesh opts out if any sun-facing sample is
                    // shadowed. This deliberately favours missing a highlight
                    // over relighting a mountain-shadowed wall at a distance.
                    foreach(var p in new[]{a,b,c,(a+b)*.5f,(b+c)*.5f,(a+c)*.5f,(a+b+c)/3})
                    {
                        if(tree.Blocked(p+normal*.12f+direction*.02f,direction)){visible=false;break;}
                        if((string?)native.Group.Attribute("id")=="object_animated_ripple.fx"&&
                            (tree.Blocked(p+normal*.12f+direction*.02f+Vector3.UnitY*.1f,direction)||tree.Blocked(p+normal*.12f+direction*.02f-Vector3.UnitY*.1f,direction)))
                        {visible=false;break;}
                    }
                }
                if(visible&&receives)mask|=1<<channel;
            }
            var key=(mesh.Material.Id,mask);
            if(!materials.TryGetValue(key,out var material))
            {
                var instance=new XElement(native!.Instance);var definitions=native.Group.Elements("SHADERINPUTDEFINITION").ToArray();
                int index=Array.FindIndex(definitions,d=>(string?)d.Attribute("name")=="MistyVisibility");
                instance.Elements("SHADERINPUT").Single(i=>(int?)i.Attribute("parameterID")==index).Value=$"{mask&1} {(mask>>1)&1} {(mask>>2)&1} 0";
                material=mesh.Material with{Id=mesh.Material.Id+"_sun"+mask,Native=native with{Instance=instance}};materials.Add(key,material);
            }
            scene.Visuals[m]=mesh with{Material=material};
            report.Add(new{mesh.Name,SourceMaterial=mesh.Material.Id,Visibility=new[]{mask&1,(mask>>1)&1,(mask>>2)&1}});
        }
        Files.Json(Path.Combine(output,"object-sun-occlusion.json"),new{Passed=true,ConservativeWholeMesh=true,TriangleSamples=7,Materials=materials.Count,Meshes=report});
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
        internal bool BlockedFootprint(Vector3 origin,Vector3 direction,Vector3 footprint)=>Visit(0,origin,direction,footprint);
        bool Visit(int id,Vector3 p,Vector3 d,Vector3 footprint=default)
        {
            var node=nodes[id];float near=.01f,far=float.PositiveInfinity;
            for(int axis=0;axis<3;axis++)
            {
                if(MathF.Abs(d[axis])<1e-8f){if(p[axis]<node.Bounds.Min[axis]-footprint[axis]||p[axis]>node.Bounds.Max[axis]+footprint[axis])return false;continue;}
                float a=(node.Bounds.Min[axis]-footprint[axis]-p[axis])/d[axis],b=(node.Bounds.Max[axis]+footprint[axis]-p[axis])/d[axis];
                near=Math.Max(near,Math.Min(a,b));far=Math.Min(far,Math.Max(a,b));if(near>far)return false;
            }
            if(node.Count==0)return Visit(node.Left,p,d,footprint)||Visit(node.Right,p,d,footprint);
            for(int i=node.Start;i<node.Start+node.Count;i++)
            {
                var t=triangles[i];
                if(footprint!=Vector3.Zero){if(IntersectsFootprint(t,p,d,footprint))return true;continue;}
                var e1=t.B-t.A;var e2=t.C-t.A;var cross=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,cross);
                if(MathF.Abs(det)<1e-8f)continue;
                var delta=p-t.A;float u=Vector3.Dot(delta,cross)/det;if(u<0||u>1)continue;
                var q=Vector3.Cross(delta,e1);float v=Vector3.Dot(d,q)/det;if(v<0||u+v>1)continue;
                if(Vector3.Dot(e2,q)/det>.01f)return true;
            }
            return false;
        }
        static bool IntersectsFootprint(Triangle t,Vector3 p,Vector3 d,Vector3 extent)
        {
            // Clip away geometry below every possible receiving point, then
            // project along the fixed sun onto the road's horizontal plane.
            float floor=p.Y-extent.Y+.01f;
            Span<Vector3> vertices=stackalloc Vector3[]{t.A,t.B,t.C};
            Span<Vector3> clipped=stackalloc Vector3[4];int count=0;
            var previous=t.C;
            foreach(var current in vertices)
            {
                bool a=previous.Y>=floor,b=current.Y>=floor;
                if(a!=b)clipped[count++]=Vector3.Lerp(previous,current,(floor-previous.Y)/(current.Y-previous.Y));
                if(b)clipped[count++]=current;
                previous=current;
            }
            if(count<3)return false;
            var size=new Vector2(extent.X+extent.Y*MathF.Abs(d.X/d.Y),extent.Z+extent.Y*MathF.Abs(d.Z/d.Y));
            Span<Vector2> polygon=stackalloc Vector2[4];
            for(int i=0;i<count;i++){var v=clipped[i]-p;polygon[i]=new(v.X-v.Y*d.X/d.Y,v.Z-v.Y*d.Z/d.Y);}
            // Separating-axis test against the filtering rectangle; unlike an
            // AABB-only projection, this preserves angled shadow boundaries.
            for(int axis=0;axis<count+2;axis++)
            {
                var edge=polygon[(axis+1)%count]-polygon[axis%count];
                var normal=axis==count?Vector2.UnitX:axis==count+1?Vector2.UnitY:new Vector2(-edge.Y,edge.X);
                float lo=float.PositiveInfinity,hi=float.NegativeInfinity;
                for(int i=0;i<count;i++){float value=Vector2.Dot(polygon[i],normal);lo=Math.Min(lo,value);hi=Math.Max(hi,value);}
                float radius=Vector2.Dot(Vector2.Abs(normal),size);
                if(lo>radius||hi< -radius)return false;
            }
            return true;
        }
    }
}
