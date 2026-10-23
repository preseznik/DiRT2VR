using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

// Static intact-pose reconstruction. Wind, tearing and entity activation are not simulated.
internal static class SourceNetting
{
    static string A(XElement element,string name)=> (string?)element.Attribute(name)
        ?? throw new InvalidDataException($"{element.Name} missing {name}");
    static float F(string value)=>float.Parse(value,CultureInfo.InvariantCulture);
    static Matrix4x4 Matrix(XElement element)
    {
        var values=element.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
        if(values.Length!=16 || values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Invalid source pole matrix");
        return new(values[0],values[1],values[2],values[3],values[4],values[5],values[6],values[7],values[8],values[9],values[10],values[11],values[12],values[13],values[14],values[15]);
    }
    static PssgFile Asset(string path)
    {
        using var input=File.OpenRead(path);bool gzip=input.ReadByte()==0x1f;input.Position=0;
        if(!gzip)return PssgFile.Open(input);
        using var decoded=new MemoryStream();
        using(var decoder=new GZipStream(input,CompressionMode.Decompress,true))decoder.CopyTo(decoded);
        decoded.Position=0;return PssgFile.Open(decoded);
    }
    internal static Scene Read(string rally,int routeIndex,Vector3 origin,string output,PikesSourceProfile? profile=null)
    {
        if(routeIndex is not (0 or 2 or 4 or 6))throw new ArgumentException("Unsupported Pikes route");
        if(!Kn5.Finite(origin))throw new ArgumentException("Nonfinite source origin");
        Files.NewOutput(output,rally);
        var scene=new Scene{Origin=origin};
        string venue=(profile??PikesSourceProfile.Modern).PathIn(rally),route="route_"+routeIndex;
        string Input(string root,string relative){string path=Files.Inside(root,relative);scene.Inputs.TryAdd(path,Files.Hash(path));return path;}
        var ens=XDocument.Load(Input(venue,route+"/objects.ens"));
        var instances=ens.Descendants().Where(e=>e.Name.LocalName is "TEMPLATEENTITYINSTANCE" or "TEMPLATEBASICENTITYINSTANCE").ToDictionary(e=>A(e,"id"));
        var references=ens.Descendants("TEMPLATEENTITYREFERENCE").ToDictionary(e=>A(e,"id"),e=>A(e,"uri"));
        var pools=ens.Descendants("TEMPLATECLOTHTYPEPOOL").ToDictionary(e=>A(e,"id"));
        var sourceSpans=ens.Descendants("TEMPLATECLOTHINSTANCE").ToArray();
        var recipes=new Dictionary<string,XDocument>();var entityFiles=new Dictionary<string,XDocument>();var assets=new Dictionary<string,PssgFile>();
        var materials=new Dictionary<string,Material>();var sourceBindings=new List<object>();var spanEvidence=new List<object>();
        PssgFile CachedAsset(string relative){if(!assets.TryGetValue(relative,out var file))assets.Add(relative,file=Asset(Input(venue,relative)));return file;}
        Material Material(string reference,string side)
        {
            if(materials.TryGetValue(reference,out var old))return old;
            int separator=reference.IndexOf('#');if(separator<=0)throw new InvalidDataException("Invalid source cloth shader reference");
            var shader=CachedAsset(reference[..separator]).GetObject<PssgShaderInstance>(reference.AsMemory(separator+1));
            var definitions=shader.GetShaderGroup().InputDefinitions.ToArray();
            var texture=shader.Inputs.Single(i=>definitions[i.ParameterId].InputName=="TDiffuseAlphaMap").Texture;
            separator=texture.IndexOf('#');if(separator<=0)throw new InvalidDataException("Invalid source cloth texture reference");
            string library=texture[..separator];if(library=="objectstextures.pssg")library="patchup_ot.pssg";
            var image=CachedAsset(library).GetObject<PssgTexture>(texture.AsMemory(separator+1));
            using var buffer=new MemoryStream();image.ToDdsFile().Write(buffer);byte[] bytes=buffer.ToArray();
            string hash=Convert.ToHexString(SHA256.HashData(bytes)),key="pikes_"+hash;
            scene.Textures.TryAdd(key,new(bytes,hash));
            var result=new Material("source-net:"+reference,"source-net-"+side,"ksPerPixelAT",0,true,new(){["txDiffuse"]=key});
            materials.Add(reference,result);
            sourceBindings.Add(new{SourceShader=reference,SourceGroup=shader.GetShaderGroup().Id,TextureReference=texture,ResolvedLibrary=library,TextureId=image.Id,image.Width,image.Height,image.TexelFormat,DdsHash=hash,TargetShader="object_simple.fx",OriginalAlphaRetained=true});
            File.WriteAllBytes(Path.Combine(output,"net-"+side+"-"+hash[..12]+".dds"),bytes);
            return result;
        }
        (string Pole,Vector3[] Points) Endpoint(XElement attachment,string[] names)
        {
            string pole=A(attachment,"instance").TrimStart('#');var instance=instances[pole];
            string reference=references[A(instance,"uri").TrimStart('#')];int separator=reference.IndexOf('#');
            if(separator<=0)throw new InvalidDataException("Invalid source pole template reference");
            string relative=reference[..separator];if(!File.Exists(Files.Inside(venue,relative)))relative=route+"/"+relative;
            if(!entityFiles.TryGetValue(relative,out var document))entityFiles.Add(relative,document=XDocument.Load(Input(venue,relative)));
            var template=document.Descendants("TEMPLATEENTITY").Single(e=>A(e,"id")==reference[(separator+1)..]);
            var world=Matrix(instance.Element("TEMPLATETRANSFORM")??throw new InvalidDataException("Pole missing transform"));
            var points=names.Select(name=>{
                var link=template.Descendants("TEMPLATELINKPOINT").Single(e=>A(e,"id")=="default_"+name+"_link");
                // Link points belong directly to the intact rigid body; no ancestor transform exists.
                if(link.Ancestors().TakeWhile(e=>e!=template).Any(e=>e.Element("TEMPLATETRANSFORM") is not null))throw new InvalidDataException("Unexpected pole link-point hierarchy transform");
                var local=Matrix(link.Element("TEMPLATETRANSFORM")??throw new InvalidDataException("Link point missing transform"));
                return Vector3.Transform(local.Translation,world)-origin;
            }).ToArray();
            return (pole,points);
        }
        foreach(var span in sourceSpans)
        {
            string id=A(span,"id"),poolId=A(span,"uri").TrimStart('#');var pool=pools[poolId];string file=A(pool,"file");
            if(file!="polemesh.xml")throw new InvalidDataException("Unsupported source cloth recipe: "+file);
            if(!recipes.TryGetValue(file,out var recipe)){
                using var stream=File.OpenRead(Input(rally,"tracks/generic/"+file));recipe=XDocument.Parse(new XmlFile(stream).Document.OuterXml);recipes.Add(file,recipe);recipe.Save(Path.Combine(output,file));
            }
            var vertices=recipe.Descendants("CLOTHVERTEX").Select(e=>new Vector3(F(A(e,"x")),F(A(e,"y")),F(A(e,"z")))).ToArray();
            var named=recipe.Descendants("CLOTHVERTEXNAME").ToDictionary(e=>A(e,"name"),e=>int.Parse(A(e,"index"),CultureInfo.InvariantCulture));
            var render=recipe.Descendants("CLOTHRENDERQUADVERTEX").ToArray();
            if(vertices.Length!=24||render.Length!=24||vertices.Any(v=>v.Z!=0))throw new InvalidDataException("Unsupported source net recipe");
            var attachments=span.Elements("TEMPLATECLOTHATTACHINSTANCE").ToDictionary(e=>A(e,"name"));
            if(attachments.Count!=2)throw new InvalidDataException("Source net span needs two poles");
            var left=Endpoint(attachments["leftA"],Enumerable.Range(0,4).Select(i=>"leftA"+i).ToArray());
            var right=Endpoint(attachments["rightA"],Enumerable.Range(0,4).Select(i=>"rightA"+i).ToArray());
            var positions=new Vector3[24];var rows=new HashSet<int>();
            for(int row=0;row<4;row++){
                int first=named["leftA"+row],last=named["rightA"+row];
                float x0=vertices[first].X,x1=vertices[last].X,y=vertices[first].Y;
                if(x1<=x0||vertices[last].Y!=y)throw new InvalidDataException("Invalid source net endpoints");
                for(int i=0;i<24;i++)if(vertices[i].Y==y){positions[i]=Vector3.Lerp(left.Points[row],right.Points[row],(vertices[i].X-x0)/(x1-x0));rows.Add(i);}
                positions[first]=left.Points[row];positions[last]=right.Points[row];
            }
            if(rows.Count!=24||positions.Any(p=>!Kn5.Finite(p)))throw new InvalidDataException("Unresolved source net vertices");
            var links=recipe.Descendants("CLOTHLINK").Select(e=>(First:int.Parse(A(e,"vert0"),CultureInfo.InvariantCulture),Last:int.Parse(A(e,"vert1"),CultureInfo.InvariantCulture))).ToArray();
            var indices=new List<ushort>();
            foreach(var quad in recipe.Descendants("CLOTHRENDERQUAD")){
                var edges=Enumerable.Range(0,4).Select(n=>int.Parse(A(quad,"link"+n),CultureInfo.InvariantCulture)).Select(n=>{
                    if(n==0||Math.Abs(n)>links.Length)throw new InvalidDataException("Invalid signed cloth link");var edge=links[Math.Abs(n)-1];return n>0?edge:(edge.Last,edge.First);
                }).ToArray();
                for(int n=0;n<4;n++)if(edges[n].Item2!=edges[(n+1)%4].Item1)throw new InvalidDataException("Source cloth quad is not closed");
                int a=edges[0].Item1,b=edges[1].Item1,c=edges[2].Item1,d=edges[3].Item1;
                foreach(int index in new[]{a,b,c,a,c,d})indices.Add(checked((ushort)index));
            }
            if(indices.Count!=90)throw new InvalidDataException("Unexpected original net topology");
            for(int side=0;side<2;side++){
                string label=side==0?"front":"back";var triangles=indices.ToArray();
                if(side==1)for(int n=0;n<triangles.Length;n+=3)(triangles[n+1],triangles[n+2])=(triangles[n+2],triangles[n+1]);
                var uv=render.Select(e=>new Vector2(F(A(e,"u"+side)),F(A(e,"v"+side)))).ToArray();
                var normals=new Vector3[24];var tangents=new Vector3[24];
                for(int n=0;n<triangles.Length;n+=3){int a=triangles[n],b=triangles[n+1],c=triangles[n+2];var ab=positions[b]-positions[a];var ac=positions[c]-positions[a];var normal=Vector3.Cross(ab,ac);var duv=uv[b]-uv[a];var dvv=uv[c]-uv[a];float determinant=duv.X*dvv.Y-duv.Y*dvv.X;
                    if(normal.LengthSquared()<1e-12f||MathF.Abs(determinant)<1e-8f)throw new InvalidDataException("Degenerate source net triangle");
                    var tangent=(ab*dvv.Y-ac*duv.Y)/determinant;foreach(int i in new[]{a,b,c}){normals[i]+=normal;tangents[i]+=tangent;}
                }
                for(int i=0;i<24;i++){normals[i]=Vector3.Normalize(normals[i]);tangents[i]=Vector3.Normalize(tangents[i]-normals[i]*Vector3.Dot(normals[i],tangents[i]));}
                scene.Visuals.Add(new("source-net:"+id+":"+label,Material(A(pool,"shader"+label),label),false,positions,normals,uv,tangents,triangles));
            }
            spanEvidence.Add(new{Id=id,Pool=poolId,LeftPole=left.Pole,RightPole=right.Pole,SourceStartsActive=A(span,"startsActive"),SourceCollisionDetection=A(span,"collisionDetection"),VerticesPerFace=24,TrianglesPerFace=30,LeftAnchors=left.Points.Select(p=>new[]{p.X,p.Y,p.Z}),RightAnchors=right.Points.Select(p=>new[]{p.X,p.Y,p.Z})});
        }
        foreach(var (relative,document) in entityFiles)
        foreach(var template in document.Descendants("TEMPLATEENTITY").Where(e=>A(e,"id").StartsWith("pole_mesh",StringComparison.Ordinal)))
            template.Save(Path.Combine(output,"source-"+A(template,"id").Replace('!','_')+".xml"));
        Files.Json(Path.Combine(output,"netting.json"),new{SourceRoute=route,Origin=new[]{origin.X,origin.Y,origin.Z},SourceSpans=sourceSpans.Length,Meshes=scene.Visuals.Count,Vertices=scene.Visuals.Sum(m=>m.Positions.Length),Triangles=scene.Visuals.Sum(m=>m.Indices.Length/3),CollisionTriangles=scene.Collision.Count,StaticIntactPose=true,UVAndSignedLinkTopologyFromRecipe=true,InteriorVerticesInterpolatedBetweenExactRowAnchors=true,QuadTriangulation="0-1-2, 0-2-3",FrontAndBackDistinctSourceUVs=true,WindTearingAndActivationPending=true,RuntimeValidated=false,Materials=sourceBindings,Spans=spanEvidence,Inputs=scene.Inputs});
        return scene;
    }
}
