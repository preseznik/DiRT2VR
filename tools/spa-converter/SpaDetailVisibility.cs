using System.Buffers.Binary;
using System.Numerics;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class SpaDetailVisibility
{
    static int I(byte[] b,int p)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p,4));
    static float F(byte[] b,int p)=>BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(p,4));
    internal static void Write(string game,string baseline,string output)
    {
        string donor=Files.Inside(game,"tracks/london/battersea/route_1");
        byte[] native=File.ReadAllBytes(Path.Combine(donor,"track.vis")),nativeCrowd=File.ReadAllBytes(Path.Combine(donor,"crowd_standing2.bin"));
        int cursor=I(native,28),matched=0;
        for(int n=0;n<I(native,12);n++){
            int count=(int)((uint)I(native,cursor+28)>>16);
            for(int i=0;i<count;i++){int p=cursor+48+i*32;if(I(native,p+12)!=4)continue;int block=I(native,p+28);for(int k=0;k<3;k++)if(F(native,p+k*4)!=F(nativeCrowd,48+block*40+16+k*4)||F(native,p+16+k*4)!=F(nativeCrowd,48+block*40+28+k*4))throw new InvalidDataException("Native crowd visibility ABI mismatch");matched++;}
            cursor+=48+count*32;
        }
        if(matched!=I(nativeCrowd,36)||matched!=I(native,80))throw new InvalidDataException("Native crowd inventory mismatch");
        var route=Files.Pssg(Path.Combine(output,"routesplit.pssg"));var roots=route.Elements<PssgNode>().Where(n=>n.Id.StartsWith("ROOT_",StringComparison.Ordinal)).ToDictionary(n=>int.Parse(n.Id[5..]));
        byte[] old=File.ReadAllBytes(Files.Inside(baseline,"track.vis")),crowd=File.ReadAllBytes(Path.Combine(output,"crowd_standing2.bin"));
        int spatial=I(old,28),terrain=I(old,64),blocks=I(crowd,36),rows=terrain+blocks;
        if(I(old,12)!=1||roots.Count!=terrain||old.Length!=spatial+48+terrain*32)throw new InvalidDataException("Expected flat Spa visibility");
        int mask=I(old,24),maskBytes=I(old,16);
        int[] counts=Enumerable.Range(0,16).Select(i=>I(old,64+i*4)).ToArray();counts[4]=blocks;
        if(maskBytes*8<counts.Sum())throw new InvalidDataException("Visibility mask capacity");
        byte[] result=new byte[spatial+48+rows*32];old.AsSpan(0,spatial+48).CopyTo(result);
        void Put(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at,4),value);
        void Box(int at,Vector3 min,Vector3 max){for(int k=0;k<3;k++){BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at+k*4,4),min[k]);BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at+16+k*4,4),max[k]);}}
        Vector3 lo=new(float.PositiveInfinity),hi=new(float.NegativeInfinity);
        void Row(int n,Vector3 min,Vector3 max,int type,int id){int at=spatial+48+n*32;Box(at,min,max);Put(at+12,type);Put(at+28,id);lo=Vector3.Min(lo,min);hi=Vector3.Max(hi,max);}
        for(int n=0;n<terrain;n++)Row(n,roots[n].BoundingBox.BoundsMin,roots[n].BoundingBox.BoundsMax,0,n);
        for(int n=0;n<blocks;n++){int at=48+n*40;Row(terrain+n,new(F(crowd,at+16),F(crowd,at+20),F(crowd,at+24)),new(F(crowd,at+28),F(crowd,at+32),F(crowd,at+36)),4,n);}
        Box(32,lo,hi);Box(spatial,lo,hi);Put(spatial+28,rows<<16);Put(80,blocks);result.AsSpan(mask,maskBytes).Fill(255);
        File.WriteAllBytes(Path.Combine(output,"track.vis"),result);
        Files.Json(Path.Combine(output,"detail-visibility.json"),new{Passed=true,TerrainTiles=terrain,CrowdBlocks=blocks,NativeCrowdCategory=4,CorroboratedStockBlocks=matched,MaskCapacityBits=maskBytes*8,RequiredMaskBits=counts.Sum(),NewCollision=false,RuntimeValidated=false});
    }
}
