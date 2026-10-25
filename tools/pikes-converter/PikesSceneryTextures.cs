using System.Security.Cryptography;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Dds;

internal static class PikesSceneryTextures
{
    // Conservative allowlist: all road/decal and object materials (including
    // readable signs) protect every texture they use, even through an alias.
    static bool Scenery(Material material) => Visuals.Target(material) is
        "pikes_bank_rockbank47.fx" or "pikes_terrain_infield_nm.fx" or "terrain_pikes_vista.fx" or
        "pikes_canopy_facing.fx" or "pikes_canopy_sheet.fx" or "pikes_canopy_rigid.fx" or "pikes_tree_billboard.fx";

    internal static void Apply(Scene scene,string output)
    {
        var materials=scene.Visuals.Select(m=>m.Material).ToArray();
        var protectedHashes=materials.Where(m=>!Scenery(m)).SelectMany(m=>m.Textures.Values)
            .Where(scene.Textures.ContainsKey).Select(k=>scene.Textures[k].Hash).ToHashSet(StringComparer.Ordinal);
        var eligible=materials.Where(Scenery).SelectMany(m=>m.Textures.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changes=new List<object>();
        foreach(string key in eligible.Where(scene.Textures.ContainsKey))
        {
            var texture=scene.Textures[key];if(protectedHashes.Contains(texture.Hash))continue;
            byte[] reduced=Cap(texture.Dds);
            if(ReferenceEquals(reduced,texture.Dds))continue;
            string hash=Convert.ToHexString(SHA256.HashData(reduced));
            scene.Textures[key]=new(reduced,hash);
            changes.Add(new{Texture=key,Before=texture.Hash,After=hash,BytesSaved=texture.Dds.Length-reduced.Length});
        }
        Files.Json(Path.Combine(output,"scenery-textures.json"),new{MaximumDimension=2048,ProtectedHashes=protectedHashes.Order().ToArray(),Changes=changes,ExistingMipPixelsOnly=true});
    }
    internal static byte[] Cap(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false);var dds=new DdsFile(stream);
        if(dds.header.width<=2048&&dds.header.height<=2048)return bytes;
        if(dds.header.mipMapCount<=1||dds.header.depth>1||(uint)dds.header.caps2!=0)return bytes;
        int block=dds.header.ddspf.fourCC switch{0x31545844=>8,0x33545844 or 0x35545844=>16,0 when dds.header.ddspf.rGBBitCount==32=>0,_=>-1};
        if(block<0)return bytes;
        int offset=0;
        uint LevelBytes()=>block==0?checked(dds.header.width*dds.header.height*4):checked(Math.Max(1,(dds.header.width+3)/4)*Math.Max(1,(dds.header.height+3)/4)*(uint)block);
        while(dds.header.width>2048||dds.header.height>2048)
        {
            if(dds.header.mipMapCount<=1)return bytes;
            offset=checked(offset+(int)LevelBytes());
            dds.header.width=Math.Max(1,dds.header.width/2);dds.header.height=Math.Max(1,dds.header.height/2);dds.header.mipMapCount--;
        }
        if(offset>=dds.bdata.Length)throw new InvalidDataException("Truncated scenery mip chain");
        dds.bdata=dds.bdata[offset..];
        dds.header.pitchOrLinearSize=block==0?dds.header.width*4:LevelBytes();
        using var result=new MemoryStream();dds.Write(result);return result.ToArray();
    }
}
