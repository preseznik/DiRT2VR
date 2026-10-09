using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using DiRT2VR.Nordschleife;

internal static class MistyGrass
{
    sealed class Image(Texture texture)
    {
        readonly BCnEncoder.Shared.ImageFiles.DdsFile file=Load(texture);
        ColorRgba32[]? pixels;
        static BCnEncoder.Shared.ImageFiles.DdsFile Load(Texture texture){using var s=new MemoryStream(texture.Dds,false);return BCnEncoder.Shared.ImageFiles.DdsFile.Load(s);}
        internal ColorRgba32 Sample(Vector2 uv)
        {
            pixels??=new BcDecoder().DecodeAllMipMaps(file)[0];
            int w=(int)file.header.dwWidth,h=(int)file.header.dwHeight;
            int x=(int)((uv.X-MathF.Floor(uv.X))*w),y=(int)((uv.Y-MathF.Floor(uv.Y))*h);
            return pixels[y*w+x];
        }
    }
    static (int X,int Z) Cell(Vector3 p)=>((int)MathF.Floor(p.X/16),(int)MathF.Floor(p.Z/16));
    internal static void Add(Scene scene,string ac,string game,string output)
    {
        var grounds=scene.Visuals.Where(m=>m.Material.Name=="grass_terrain_new1a").ToArray();
        if(grounds.Length==0)throw new InvalidDataException("No source ground for grass placement.");
        // Use the original plant sheet embedded in Misty Loch's own trees KN5.
        // Its nonuniform cards have authored alpha and colours, not CSP markers.
        const string texture="foliage.dds";
        if(!scene.Textures.ContainsKey(texture))throw new InvalidDataException("Misty Loch's plant sheet is missing.");
        Vector4[] grassCards=[new(.44f,.045f,.70f,.14f),new(.50f,.635f,.815f,.747f),new(.425f,.76f,.665f,.87f),new(.68f,.775f,.89f,.866f),new(.002f,.88f,.295f,.997f),new(.414f,.89f,.67f,.997f)];
        Vector4[] flowerCards=[new(.735f,.001f,.855f,.16f),new(.15f,.635f,.319f,.742f),new(.735f,.505f,.86f,.625f)];
        var material=new MistyNativeMaterials(scene,game).Detail(new("misty:grass","misty_generated_grass","ksGrass",0,true,new(){["txDiffuse"]=texture}),grass:true);
        var mask=new Image(scene.Textures[grounds[0].Material.Textures["txMask"]]);
        var gates=scene.Gates.GroupBy(g=>Cell(g.Position)).ToDictionary(g=>g.Key,g=>g.ToArray());
        var cover=new Dictionary<(int,int),List<(Vector3 A,Vector3 B,Vector3 C)>>();
        foreach(var tri in scene.Collision.Where(t=>t.Material is "RDT+" or "CON+"))
        {
            var a=tri.Position0;var b=tri.Position1;var c=tri.Position2;
            if(MathF.Abs((b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X))<.001f)continue;
            var lo=Cell(Vector3.Min(a,Vector3.Min(b,c)));var hi=Cell(Vector3.Max(a,Vector3.Max(b,c)));
            for(int x=lo.X;x<=hi.X;x++)for(int z=lo.Z;z<=hi.Z;z++){if(!cover.TryGetValue((x,z),out var list))cover[(x,z)]=list=[];list.Add((a,b,c));}
        }
        bool Covered(Vector3 p)
        {
            if(!cover.TryGetValue(Cell(p),out var triangles))return false;
            foreach(var (a,b,c) in triangles)
            {
                float det=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);
                float u=((b.Z-c.Z)*(p.X-c.X)+(c.X-b.X)*(p.Z-c.Z))/det;
                float v=((c.Z-a.Z)*(p.X-c.X)+(a.X-c.X)*(p.Z-c.Z))/det;
                if(u<-.01f||v<-.01f||u+v>1.01f)continue;
                float y=u*a.Y+v*b.Y+(1-u-v)*c.Y;
                if(y>=p.Y-.10f&&y<=p.Y+4)return true;
            }
            return false;
        }
        var positions=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<ushort>();var colours=new List<Vector4>();
        var random=new Random(50715);int clumps=0,flowers=0,batches=0,covered=0;var samples=new List<object>();
        void Flush()
        {
            if(positions.Count==0)return;
            scene.Visuals.Add(new("misty_grass_"+batches++,material,false,positions.ToArray(),normals.ToArray(),uv.ToArray(),Enumerable.Repeat(Vector3.UnitX,positions.Count).ToArray(),indices.ToArray()){Colors=colours.ToArray(),LodOut=105});
            positions.Clear();normals.Clear();uv.Clear();indices.Clear();colours.Clear();
        }
        foreach(var mesh in grounds)for(int t=0;t<mesh.Indices.Length;t+=3)
        {
            int ia=mesh.Indices[t],ib=mesh.Indices[t+1],ic=mesh.Indices[t+2];var a=mesh.Positions[ia];var b=mesh.Positions[ib];var c=mesh.Positions[ic];
            var cross=Vector3.Cross(b-a,c-a);float area=cross.Length()*.5f;
            if(area<.01f||MathF.Abs(cross.Y)/Math.Max(.001f,cross.Length())<.78f)continue;
            int attempts=(int)Math.Min(18000,area*.82f+random.NextDouble());
            for(int attempt=0;attempt<attempts;attempt++)
            {
                float u=MathF.Sqrt(random.NextSingle()),v=random.NextSingle();var p=a*(1-u)+b*(u*(1-v))+c*(u*v);
                var cell=Cell(p);Gate? nearest=null;float distance=float.MaxValue;
                for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)if(gates.TryGetValue((cell.X+x,cell.Z+z),out var candidates))foreach(var g in candidates)
                {
                    float d=Vector2.DistanceSquared(new(p.X,p.Z),new(g.Position.X,g.Position.Z));if(d<distance){distance=d;nearest=g;}
                }
                if(nearest is null||distance>MathF.Pow(Math.Min(12,Math.Max(nearest.Left,nearest.Right))+6,2)||MathF.Abs(p.Y-nearest.Position.Y)>4)continue;
                var sourceUV=mesh.UV[ia]*(1-u)+mesh.UV[ib]*(u*(1-v))+mesh.UV[ic]*(u*v);var m=mask.Sample(sourceUV);
                if(m.r>150||m.g+m.b<100)continue;
                if(Covered(p)){covered++;continue;}
                if(clumps>=60000)throw new InvalidDataException("Generated grass exceeds the diagnostic budget.");
                var rect=grassCards[random.Next(grassCards.Length)];float h=.18f+random.NextSingle()*.24f,w=.40f+random.NextSingle()*.30f;
                if(random.NextDouble()<.055){rect=flowerCards[random.Next(flowerCards.Length)];h*=1.5f;w*=.65f;flowers++;}
                // The authored plant sheet already carries its colour/shading.
                // Applying the terrain's baked darkness again crushes thin blades.
                float angle=random.NextSingle()*MathF.PI;
                float tone=.85f+random.NextSingle()*.20f;
                var colour=new Vector4(tone,tone,tone,1);
                if(positions.Count+8>60000)Flush();
                for(int card=0;card<2;card++)
                {
                    float yaw=angle+card*MathF.PI*.5f;var side=new Vector3(MathF.Cos(yaw),0,MathF.Sin(yaw))*w*.5f;
                    int start=positions.Count;var root=p-Vector3.UnitY*.025f;
                    positions.AddRange([root-side,root+side,root+side+Vector3.UnitY*h,root-side+Vector3.UnitY*h]);
                    var rootColour=new Vector4(colour.X*.70f,colour.Y*.70f,colour.Z*.70f,1);
                    normals.AddRange(Enumerable.Repeat(Vector3.UnitY,4));colours.AddRange([rootColour,rootColour,colour,colour]);
                    float x0=rect.X,x1=rect.Z,y0=rect.Y,y1=rect.W;
                    uv.AddRange([new(x0,y1),new(x1,y1),new(x1,y0),new(x0,y0)]);
                    foreach(int i in new[]{0,1,2,0,2,3})indices.Add(checked((ushort)(start+i)));
                }
                if(samples.Count<128)samples.Add(new{Position=new[]{p.X,p.Y,p.Z},nearest.Distance});clumps++;
            }
        }
        Flush();
        if(clumps<1000)throw new InvalidDataException("Unexpectedly empty grass reconstruction.");
        Files.Json(Path.Combine(output,"grass-audit.json"),new{Passed=true,Clumps=clumps,FlowerClumps=flowers,Triangles=clumps*4,Batches=batches,OccludedSamplesRejected=covered,OriginalGroundOnly=true,CollisionAdded=false,MaximumRoadsideReach=6,FadeMetres=new[]{70,105},Atlas="rt_misty_loch_trees.kn5/"+texture,AtlasHash=scene.Textures[texture].Hash,OriginalAtlasBytesRetained=true,GrassCards=grassCards.Select(r=>new[]{r.X,r.Y,r.Z,r.W}),FlowerCards=flowerCards.Select(r=>new[]{r.X,r.Y,r.Z,r.W}),Samples=samples});
        Console.WriteLine($"Generated {clumps:N0} grass/flower clumps; {covered:N0} paved samples rejected.");
    }
}
