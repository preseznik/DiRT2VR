using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class SpaArmcoSupport
{
    // Include fence ends beyond the old 25 m prop limit now that the adjoining
    // 50 m grass envelope is supported. Shapes use the source's initial state.
    internal static void Add(Scene scene,string source,Func<Vector3,float,bool> near,string output)
    {
        string venue=Files.Inside(source,"tracks/locations/europe/spa_francorchamps");
        string Input(string name){string p=Files.Inside(venue,name);scene.Inputs.TryAdd(p,Files.Hash(p));return p;}
        var assets=new Dictionary<string,PssgFile>();
        PssgFile Asset(string name){if(!assets.TryGetValue(name,out var file))assets[name]=file=Files.Pssg(Input(name));return file;}
        Matrix4x4 Frame(string file,string id,string sid)=>SpaCollisionInspect.World(SpaCollisionInspect.Nodes(Asset(file).GetObject<PssgNode>(id.AsMemory()))
            .Single(n=>(n.Attributes.FirstOrDefault(a=>a.Name=="nickname")?.DisplayValue??n.Id)==sid));
        var expected=new Scene{Origin=scene.Origin};var primitives=new SpaPrimitives(Input,Frame,near,expected,50);
        var seen=new HashSet<(string,Matrix4x4)>();int placements=0;
        void Add(string model,Matrix4x4 matrix,string label){
            if(!model.StartsWith("armco_",StringComparison.Ordinal)||!seen.Add((model,matrix)))return;
            placements++;primitives.Add(model,"objects.pssg",matrix,label);
        }
        byte[] bytes=File.ReadAllBytes(Input("route_0/ornaments.bin"));int I(int p)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p,4));float F(int p)=>BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(p,4));
        int list=I(4),refs=I(list+36),instances=I(list+44);var names=new Dictionary<int,string>();
        for(int i=0;i<I(list+40);i++){int p=refs+i*48,n=I(p);names.Add(I(p+4),Encoding.ASCII.GetString(bytes,n,Array.IndexOf(bytes,(byte)0,n)-n));}
        for(int i=0;i<I(list+48);i++){int p=instances+i*116;string name=names[I(p)];if(!name.StartsWith("armco_",StringComparison.Ordinal))continue;
            var m=new Matrix4x4(F(p+8),F(p+12),F(p+16),0,F(p+20),F(p+24),F(p+28),0,F(p+32),F(p+36),F(p+40),0,F(p+44),F(p+48),F(p+52),1);Add(name,m,name+":"+I(p+4));}
        var ens=XDocument.Load(Input("route_0/objects.ens"));
        var references=ens.Descendants("TEMPLATEENTITYREFERENCE").ToDictionary(e=>(string)e.Attribute("id")!,e=>((string)e.Attribute("uri")!).Split('#').Last());
        foreach(var e in ens.Descendants().Where(e=>e.Name.LocalName is "TEMPLATEENTITYINSTANCE" or "TEMPLATEBASICENTITYINSTANCE")){
            if(!string.IsNullOrEmpty((string?)e.Attribute("mode_layer")))continue;
            string name=((string)e.Attribute("uri")!)[1..];name=references.GetValueOrDefault(name,name);if(name.EndsWith(".max",StringComparison.Ordinal))name=name[..^4];
            if(!name.StartsWith("armco_",StringComparison.Ordinal))continue;
            var f=e.Element("TEMPLATETRANSFORM")!.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
            Add(name,new(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]),(string)e.Attribute("id")!);
        }
        // Native quadtree vertices are quantized. Compare cyclic triangle winding
        // within the existing 2 cm readback tolerance instead of exact float keys.
        (int,int,int) Cell(Vector3 p)=>((int)MathF.Floor(p.X/.04f),(int)MathF.Floor(p.Y/.04f),(int)MathF.Floor(p.Z/.04f));
        var bins=new Dictionary<(int,int,int),List<QuadTreeDataTriangle>>();
        foreach(var t in scene.Collision.Where(t=>t.Material=="METL")){var c=Cell((t.Position0+t.Position1+t.Position2)/3);if(!bins.TryGetValue(c,out var listFaces))bins[c]=listFaces=[];listFaces.Add(t);}
        bool Close(Vector3 a,Vector3 b)=>Vector3.DistanceSquared(a,b)<=.02f*.02f;
        bool Present(QuadTreeDataTriangle t){var (cx,cy,cz)=Cell((t.Position0+t.Position1+t.Position2)/3);
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)if(bins.TryGetValue((cx+x,cy+y,cz+z),out var listFaces))foreach(var b in listFaces)
                if(Close(t.Position0,b.Position0)&&Close(t.Position1,b.Position1)&&Close(t.Position2,b.Position2)||Close(t.Position0,b.Position1)&&Close(t.Position1,b.Position2)&&Close(t.Position2,b.Position0)||Close(t.Position0,b.Position2)&&Close(t.Position1,b.Position0)&&Close(t.Position2,b.Position1))return true;
            return false;}
        var added=expected.Collision.DistinctBy(GeometryCoverage.Exact).Where(t=>!Present(t)).ToArray();scene.Collision.AddRange(added);
        Files.Json(Path.Combine(output,"armco-support.json"),new{SourcePlacements=placements,ExpectedInitialStateTriangles=expected.Collision.Count,AddedSourceTriangles=added.Length,CorridorMetres=50,SourceDimensionsAndBodyFramesRetained=true,InactiveDamageBodiesExcluded=true,ExistingCollisionUnchanged=true,RuntimeValidated=false});
        SpaCollisionInspect.Dump(Path.Combine(output,"added-armco.bin"),added);
    }
}
