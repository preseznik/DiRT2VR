using System.Numerics;
using System.Buffers.Binary;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Source cards and terrain generation patches. These are not placed grass blades.
// Keep the packed generation fields intact until the source runtime ABI is known.
internal static class SpaGrass
{
    internal sealed record Card(string Type,string Name,Vector3[] Positions,Vector3[] TextureCoordinates,ushort[] Indices);
    internal sealed record GrassType(string Name,string[] Variants,float Parameter);
    internal sealed record Patch(int Number,Bounds Bounds,ushort VertexCount,ushort TriangleCount,byte[] Reserved,byte[] PackedVertices,byte[] PackedTriangles,byte[] Padding)
    {
        // Corroborated against all 23 unique original terrain vertices in cell 0:
        // max XZ error 0.000184 m, Y error within one byte quantization interval.
        internal Vector3 Position(int vertex)
        {
            if((uint)vertex>=VertexCount)throw new ArgumentOutOfRangeException(nameof(vertex));
            var bytes=PackedVertices.AsSpan(vertex*8,8);var fraction=new Vector3(BinaryPrimitives.ReadUInt16LittleEndian(bytes)/65535f,bytes[4]/255f,BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..])/65535f);
            return Bounds.Min+(Bounds.Max-Bounds.Min)*fraction;
        }
        internal (byte A,byte B,byte C,int Type) Triangle(int triangle)
        {
            if((uint)triangle>=TriangleCount)throw new ArgumentOutOfRangeException(nameof(triangle));
            var bytes=PackedTriangles.AsSpan(triangle*4,4);
            // The source stores eight distinct type values at increments of 8.
            return(bytes[0],bytes[1],bytes[2],(bytes[3]>>3)&7);
        }
    }
    internal sealed record Library(Card[] Cards,GrassType[] Types,Patch[] Patches,byte[] ParameterTable,Texture Atlas,Dictionary<string,string> Inputs);

    internal static Library Read(string rally,string scratchDirectory)
    {
        string venue=Files.Inside(rally,"tracks/locations/europe/spa_francorchamps");
        string pssgPath=Files.Inside(venue,"ground_cover.pssg"),grsPath=Files.Inside(venue,"grass.grs");
        var inputs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{pssgPath,Files.Hash(pssgPath)},{grsPath,Files.Hash(grsPath)}};
        var file=Files.Pssg(pssgPath);
        var shaders=file.Elements<PssgShaderInstance>().ToArray();
        if(shaders.Length!=1||shaders[0].GetShaderGroup().Id!="ground_cover_foliage_billboard.fx")throw new InvalidDataException("Unexpected source ground-cover material library");
        var diffuse=shaders[0].Inputs.Single(i=>i.Type=="texture");
        if(diffuse.Texture!="#gc_billboard01.tga")throw new InvalidDataException("Unexpected source ground-cover texture binding");
        var texture=file.GetObject<PssgTexture>(diffuse.Texture.AsMemory(1));
        using var dds=new MemoryStream();texture.ToDdsFile().Write(dds);byte[] bytes=dds.ToArray();
        var atlas=new Texture(bytes,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        var cards=file.Elements<PssgRenderStreamInstance>().Select(draw=>
        {
            if(draw.ParentElement is not PssgRenderNode node||node.ParentElement is not PssgNode type||!type.Id.StartsWith("type_",StringComparison.Ordinal))throw new InvalidDataException("Unexpected source card hierarchy");
            var rds=draw.GetRenderDataSource();var streams=rds.Streams.ToArray();
            if(streams.Length!=2)throw new InvalidDataException("Unexpected source card stream count");
            Vector3[] Vectors(PssgRenderStream stream,string expected)
            {
                var block=stream.GetDataBlock();var field=block.Streams.ElementAt(checked((int)stream.SubStream));
                if(field.RenderType!=expected||field.DataType!="float3"||field.Offset!=0||field.Stride!=12||block.ElementCount!=4||block.Size!=48||block.Data.Value.Length!=48)throw new InvalidDataException("Unexpected source card float3 layout");
                return Enumerable.Range(0,4).Select(i=>new Vector3(F32BE(block.Data.Value,i*12),F32BE(block.Data.Value,i*12+4),F32BE(block.Data.Value,i*12+8))).ToArray();
            }
            var positions=Vectors(streams[0],"Vertex");var coordinates=Vectors(streams[1],"ST");
            var index=rds.IndexSource??throw new InvalidDataException("Missing source card indices");
            if(index.Primitive!="triangles"||index.Format!="ushort"||index.Count!=6||index.Data.Value.Length!=12)throw new InvalidDataException("Unexpected source card primitive");
            ushort[] indices=Enumerable.Range(0,6).Select(i=>BinaryPrimitives.ReadUInt16BigEndian(index.Data.Value.AsSpan(i*2,2))).ToArray();
            if(indices.Any(i=>i>=4))throw new InvalidDataException("Out-of-range source card index");
            // Authoring-grid NODE translations display the catalogue; they are
            // not blade placements. Preserve the local card vertices verbatim.
            return new Card(type.Id,node.Id,positions,coordinates,indices);
        }).ToArray();
        byte[] grs=File.ReadAllBytes(grsPath);int cursor=0;
        uint U32(){uint value=BinaryPrimitives.ReadUInt32LittleEndian(grs.AsSpan(cursor,4));cursor+=4;return value;}
        float Float(){float value=BitConverter.Int32BitsToSingle(unchecked((int)U32()));if(!float.IsFinite(value))throw new InvalidDataException("Nonfinite source grass parameter");return value;}
        string Name(){var value=grs.AsSpan(cursor,16);int end=value.IndexOf((byte)0);if(end<1||value[(end+1)..].ContainsAnyExcept((byte)0))throw new InvalidDataException("Invalid source grass name slot");cursor+=16;return System.Text.Encoding.ASCII.GetString(value[..end]);}
        if(U32()!=7||U32()!=8)throw new InvalidDataException("Only the original Spa version-7 eight-type grass file is supported");
        int patchCount=unchecked((int)U32());if(patchCount!=1080||U32()!=0)throw new InvalidDataException("Unexpected source grass header");
        byte[] parameters=grs.AsSpan(cursor,2400).ToArray();cursor+=2400;
        var types=new GrassType[8];
        for(int t=0;t<types.Length;t++)
        {
            string[] slots=new string[8];for(int j=0;j<8;j++)
            {
                if(grs[cursor]==0){if(grs.AsSpan(cursor,16).ContainsAnyExcept((byte)0))throw new InvalidDataException("Nonempty source variant padding");cursor+=16;slots[j]="";}
                else slots[j]=Name();
            }
            uint count=U32();float parameter=Float();string name=Name();
            if(count>8||slots.Take((int)count).Any(string.IsNullOrEmpty)||slots.Skip((int)count).Any(s=>s.Length!=0))throw new InvalidDataException("Unexpected source grass variant count");
            if(name!="type_"+(t+1).ToString("D2"))throw new InvalidDataException("Unexpected source grass type ordering");
            types[t]=new(name,slots.Take((int)count).ToArray(),parameter);
        }
        if(U32()!=8)throw new InvalidDataException("Unexpected source grass type-table trailer");
        int[] offsets=Enumerable.Range(0,patchCount).Select(_=>unchecked((int)U32())).ToArray();
        if(offsets[0]!=((cursor+15)&~15)||grs.AsSpan(cursor,offsets[0]-cursor).ContainsAnyExcept((byte)0))throw new InvalidDataException("Unexpected source cell alignment");
        var patches=new Patch[patchCount];
        for(int p=0;p<patches.Length;p++)
        {
            cursor=offsets[p];int end=p+1<offsets.Length?offsets[p+1]:grs.Length;
            if(end<=cursor)throw new InvalidDataException("Source grass offsets are not increasing");
            ushort vertices=BinaryPrimitives.ReadUInt16LittleEndian(grs.AsSpan(cursor,2)),triangles=BinaryPrimitives.ReadUInt16LittleEndian(grs.AsSpan(cursor+2,2));cursor+=4;
            byte[] reserved=grs.AsSpan(cursor,12).ToArray();cursor+=12;
            var min=new Vector3(Float(),Float(),Float());var max=new Vector3(Float(),Float(),Float());
            if(min.X>max.X||min.Y>max.Y||min.Z>max.Z)throw new InvalidDataException("Inverted source grass patch bounds");
            byte[] packedVertices=grs.AsSpan(cursor,vertices*8).ToArray();cursor+=vertices*8;
            byte[] packedTriangles=grs.AsSpan(cursor,triangles*4).ToArray();cursor+=triangles*4;
            for(int j=0;j<triangles;j++){var triangle=packedTriangles.AsSpan(j*4,4);if(triangle[0]>=vertices||triangle[1]>=vertices||triangle[2]>=vertices||(triangle[3]&0xc7)!=0x87)throw new InvalidDataException("Unexpected source grass triangle record");}
            if(cursor>end||end-cursor>=16||grs.AsSpan(cursor,end-cursor).ContainsAnyExcept((byte)0))throw new InvalidDataException("Unexpected source grass patch padding");
            patches[p]=new(p,new(min,max),vertices,triangles,reserved,packedVertices,packedTriangles,grs.AsSpan(cursor,end-cursor).ToArray());
        }
        if(types.SelectMany(t=>t.Variants.Select(v=>(t.Name,v))).Order().SequenceEqual(cards.Select(c=>(c.Type,c.Name)).Order())!=true)throw new InvalidDataException("Source grass variants do not match the original card library");
        return new(cards,types,patches,parameters,atlas,inputs);
    }
    static float F32BE(byte[] bytes,int offset)
    {
        float value=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset,4)));
        if(!float.IsFinite(value))throw new InvalidDataException("Nonfinite source card attribute");return value;
    }

    // Optional static visual approximation. The source file provides generation
    // substrate, not runtime blade centres; chosen sampling never claims parity.
    internal static Scene ReadBaked(string rally,string output,Vector3 origin,Gate[] gates,float radius=25,float density=.5f,int maximumCards=100000,int batchTileSize=64)
    {
        if(gates.Length<2||!float.IsFinite(radius)||radius<=0||!float.IsFinite(density)||density<=0||maximumCards<1||batchTileSize<32||batchTileSize>1024)throw new ArgumentException("Invalid bounded grass bake parameters");
        var library=Read(rally,output);var scene=new Scene{Origin=origin,Gates=gates,FullCourse=true};
        foreach(var input in library.Inputs)scene.Inputs.Add(input.Key,input.Value);
        string textureKey="spa_grass_"+library.Atlas.Hash;scene.Textures.Add(textureKey,library.Atlas);
        var material=new Material("spa_source_grass_static","spa_source_grass_static","ksTree",0,true,new(){{"txDiffuse",textureKey}});
        var cards=library.Cards.ToDictionary(c=>c.Name,StringComparer.Ordinal);
        const int cellSize=32;var road=new Dictionary<(int X,int Z),List<(Vector3 A,Vector3 B)>>();
        for(int i=1;i<gates.Length;i++)
        {
            var a=gates[i-1].Position;var b=gates[i].Position;
            int x0=(int)MathF.Floor((MathF.Min(a.X,b.X)-radius)/cellSize),x1=(int)MathF.Floor((MathF.Max(a.X,b.X)+radius)/cellSize);
            int z0=(int)MathF.Floor((MathF.Min(a.Z,b.Z)-radius)/cellSize),z1=(int)MathF.Floor((MathF.Max(a.Z,b.Z)+radius)/cellSize);
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++){if(!road.TryGetValue((x,z),out var list))road.Add((x,z),list=[]);list.Add((a,b));}
        }
        bool Near(Vector3 p)
        {
            if(!road.TryGetValue(((int)MathF.Floor(p.X/cellSize),(int)MathF.Floor(p.Z/cellSize)),out var segments))return false;
            foreach(var (a,b) in segments)
            {
                var edge=new Vector2(b.X-a.X,b.Z-a.Z);var delta=new Vector2(p.X-a.X,p.Z-a.Z);
                float t=edge.LengthSquared()>1e-8f?Math.Clamp(Vector2.Dot(delta,edge)/edge.LengthSquared(),0,1):0;
                if((delta-edge*t).LengthSquared()<=radius*radius&&MathF.Abs(p.Y-(a.Y+(b.Y-a.Y)*t))<180)return true;
            }
            return false;
        }
        var eligible=new List<(int Patch,int Triangle,int Type,Vector3 A,Vector3 B,Vector3 C,float Area)>();double area=0;
        foreach(var patch in library.Patches)
        for(int t=0;t<patch.TriangleCount;t++)
        {
            var triangle=patch.Triangle(t);var a=patch.Position(triangle.A)-origin;var b=patch.Position(triangle.B)-origin;var c=patch.Position(triangle.C)-origin;
            if(!Near(a)&&!Near(b)&&!Near(c)&&!Near((a+b+c)/3))continue;
            float triangleArea=Vector3.Cross(b-a,c-a).Length()*.5f;if(!float.IsFinite(triangleArea)||triangleArea<=1e-7f)continue;
            eligible.Add((patch.Number,t,triangle.Type,a,b,c,triangleArea));area+=triangleArea;
        }
        // Scale globally if the cap requires it, retaining all course regions.
        // The 5% headroom absorbs deterministic stochastic rounding variation.
        float effectiveDensity=area>0?(float)Math.Min(density,maximumCards*.95/area):density;
        var positions=new List<Vector3>();var normals=new List<Vector3>();var coordinates=new List<Vector2>();var tangents=new List<Vector3>();var indices=new List<ushort>();
        var placed=new List<(Vector3 Centre,Card Card,float Yaw)>();
        int count=0,rejected=0,number=0;var perType=new int[8];bool capped=false;
        void Flush()
        {
            if(positions.Count==0)return;
            scene.Visuals.Add(new Mesh("spa_source_grass_static_"+number++,material,false,positions.ToArray(),normals.ToArray(),coordinates.ToArray(),tangents.ToArray(),indices.ToArray()));
            positions.Clear();normals.Clear();coordinates.Clear();tangents.Clear();indices.Clear();
        }
        static uint Mix(uint value){value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;return value^(value>>16);}
        static float Unit(uint value)=>(value>>8)*(1f/16777216);
        foreach(var triangle in eligible)
        {
            uint seed=Mix(unchecked((uint)triangle.Patch*0x9e3779b9u^(uint)triangle.Triangle*0x85ebca6bu));
            float expected=triangle.Area*effectiveDensity;int requested=(int)MathF.Floor(expected);
            if(Unit(Mix(seed))<expected-requested)requested++;
            var type=library.Types[triangle.Type];
            for(int sample=0;sample<requested;sample++)
            {
                uint sampleSeed=Mix(seed^unchecked((uint)(sample+1)*0xc2b2ae35u));
                float root=MathF.Sqrt(Unit(Mix(sampleSeed^0x68e31da4u))),v=Unit(Mix(sampleSeed^0xb5297a4du));
                var centre=triangle.A*(1-root)+triangle.B*(root*(1-v))+triangle.C*(root*v);
                if(!Near(centre)){rejected++;continue;}
                if(count==maximumCards){capped=true;break;}
                var card=cards[type.Variants[Mix(sampleSeed^0x1b56c4e9u)%(uint)type.Variants.Length]];
                float yaw=Unit(Mix(sampleSeed^0xa511e9b3u))*MathF.Tau;
                placed.Add((centre,card,yaw));
                count++;perType[triangle.Type]++;
            }
            if(capped)break;
        }
        var spatialGroups=placed.GroupBy(p=>((int)MathF.Floor(p.Centre.X/batchTileSize),(int)MathF.Floor(p.Centre.Z/batchTileSize))).OrderBy(g=>g.Key.Item1).ThenBy(g=>g.Key.Item2).ToArray();
        foreach(var group in spatialGroups)
        {
            foreach(var (centre,card,yaw) in group)
            {
                var rotation=Matrix4x4.CreateRotationY(yaw);
                var normal=Vector3.Cross(card.Positions[card.Indices[1]]-card.Positions[card.Indices[0]],card.Positions[card.Indices[2]]-card.Positions[card.Indices[0]]);
                normal=Vector3.Normalize(Vector3.TransformNormal(normal,rotation));var tangent=Vector3.TransformNormal(Vector3.UnitX,rotation);
                if(positions.Count+card.Positions.Length>64000)Flush();ushort first=checked((ushort)positions.Count);
                for(int i=0;i<card.Positions.Length;i++){positions.Add(Vector3.Transform(card.Positions[i],rotation)+centre);normals.Add(normal);coordinates.Add(new(card.TextureCoordinates[i].X,card.TextureCoordinates[i].Y));tangents.Add(tangent);}
                foreach(ushort index in card.Indices)indices.Add(checked((ushort)(first+index)));
            }
            Flush();
        }
        Files.Json(Path.Combine(output,"source-grass-bake.json"),new{Approximation=true,SourceWorldBladeCentresRecovered=false,OriginalRuntimeSeedsUnknown=true,OriginalEligiblePatchPositionsTypesCardShapesUVAndAtlasRetained=true,ChosenSampling="Deterministic uniform barycentric samples with area density and yaw/variant selection",RequestedDensity=density,EffectiveDensity=effectiveDensity,MaximumCards=maximumCards,ActualCards=count,CardCountsByType=perType,EligibleTriangles=eligible.Count,EligibleSurfaceArea=area,RejectedOutsideCorridor=rejected,CorridorRadius=radius,CapReached=capped,Meshes=number,BatchTileSize=batchTileSize,SpatialTiles=spatialGroups.Length,Triangles=count*2,Vertices=count*4,UnknownPackedChannelsUnused=true,SourceParameterMeaningUnknown=true,StaticWindAbsent=true,CameraBillboardingAbsent=true,CollisionsAdded=0,NativeRuntimeValidated=false,Inputs=library.Inputs,Atlas=library.Atlas.Hash});
        return scene;
    }
    internal static void Inspect(string rally,string output)
    {
        Files.NewOutput(output,rally);var library=Read(rally,output);
        Files.Json(Path.Combine(output,"grass-library.json"),new{library.Types,Cards=library.Cards.Select(c=>new{c.Type,c.Name,Positions=c.Positions.Select(V),TextureCoordinates=c.TextureCoordinates.Select(V),c.Indices}),PatchCount=library.Patches.Length,Vertices=library.Patches.Sum(p=>(long)p.VertexCount),Triangles=library.Patches.Sum(p=>(long)p.TriangleCount),library.Inputs,Atlas=library.Atlas.Hash,CardVerticesAreLocal=true,SourceWorldBladeCentresRecovered=false,SourceGenerationPatchesRecovered=true,SourcePatchPositionsDecodedWithinOriginalQuantization=true,Unknowns=new[]{"Vertex byte5 and tail ushort runtime meaning","Runtime generation density and scatter","Source blade colour and normal generation","DiRT 2 version-4 grass compatibility"}});
        File.WriteAllBytes(Path.Combine(output,"gc_billboard_d.dds"),library.Atlas.Dds);
        static float[] V(Vector3 value)=>[value.X,value.Y,value.Z];
    }
}






