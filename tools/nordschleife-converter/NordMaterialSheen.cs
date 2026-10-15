using System.Buffers.Binary;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Nordschleife;

internal static class NordMaterialSheen
{
    internal static byte[] Patch(string game,string layout,int percent,PackReceipt receipt)
    {
        string relative=RoadSheen.Target(layout,true),path=SafeFiles.Inside(game,relative);
        var entry=receipt.Files.SingleOrDefault(f=>f.Path==relative);
        if(entry is null||entry.Bytes>RoadSheen.MaxMaterialBytes||new FileInfo(path).Length!=entry.Bytes||SafeFiles.Hash(path)!=entry.Sha256)
            throw new IOException("Nordschleife road material is missing or changed. Restore original files or rebuild the track.");
        var route=RoadSheenPssg.Read(path,RoadSheen.MaxMaterialBytes);
        var group=route.All("SHADERGROUP").SingleOrDefault(g=>g.Text("id")=="terrain_nord_ground.fx");
        var definitions=group?.Children.Where(n=>n.Name=="SHADERINPUTDEFINITION").ToArray()??[];
        int parameter=Array.FindIndex(definitions,d=>d.Text("name")=="NordRoadSheen");
        if(parameter<0)throw new IOException("Unsupported Nordschleife road material. Rebuild the track.");
        byte[] bytes=File.ReadAllBytes(path);int roads=0;
        foreach(var input in route.All("SHADERINSTANCE").Where(m=>m.Text("shaderGroup")=="#terrain_nord_ground.fx")
            .SelectMany(m=>m.Children.Where(i=>i.Name=="SHADERINPUT"&&i.Number("parameterID")==parameter)))
        {
            if(input.Text("type")!="constant"||input.Text("format")!="float4"||input.Bytes!=16)throw new IOException("Invalid Nordschleife road sheen parameters.");
            int at=checked((int)input.Data);float marker=BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(at+12,4));
            if(marker==0)continue; // Grass, kerbs and other ground never receive road sheen.
            if(marker!=1)throw new IOException("Invalid Nordschleife road marker.");
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(at,4),percent/100f);
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(at+8,4),1);roads++;
        }
        if(roads is <1 or >16)throw new IOException("Unexpected Nordschleife road material inventory.");
        return bytes;
    }
}
