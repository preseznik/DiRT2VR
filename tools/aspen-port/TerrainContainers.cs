using System.Xml.Linq;
using System.Buffers.Binary;

// DiRT 2 registers land.pssg and routesplit.pssg as terrain scenes. Its
// tracksplit.pssg load supplies shared resources, not additional terrain tiles.
internal static class TerrainContainers
{
    internal static object Convert(string track)
    {
        var path=Path.Combine(track,"tracksplit.pssg");
        var landPath=Path.Combine(track,"land.pssg");
        if(PortFiles.ReadPssg(landPath).Descendants("RENDERDATASOURCE").Any()) throw new InvalidDataException("Expected Aspen's empty land container.");
        var resources=PortFiles.ReadPssg(path); var scene=new XDocument(resources);
        int CountTiles(XDocument doc) => doc.Descendants("NODE").Count(e => ((string?)e.Attribute("id"))?.StartsWith("ROOT_",StringComparison.Ordinal)==true || ((string?)e.Attribute("id"))?.StartsWith("LAND_",StringComparison.Ordinal)==true);
        int tiles=CountTiles(scene);
        int routeTiles=CountTiles(PortFiles.ReadPssg(Path.Combine(track,"route_0","routesplit.pssg")));
        var visibility=File.ReadAllBytes(Path.Combine(track,"route_0","track.vis"));
        if(visibility.Length<128 || BinaryPrimitives.ReadInt32LittleEndian(visibility.AsSpan(64)) != tiles+routeTiles)
            throw new InvalidDataException("Terrain registration count does not match Aspen visibility identities.");
        foreach(var material in scene.Descendants("SHADERINSTANCE"))
        foreach(var input in material.Elements("SHADERINPUT"))
            if(input.Attribute("texture") is { } texture && texture.Value.StartsWith('#'))
                texture.Value=(((string?)material.Attribute("id"))?.StartsWith("aspen_static_",StringComparison.Ordinal)==true ? "objects.pssg" : "tracksplit.pssg")+texture.Value;
        foreach(var texture in scene.Descendants("TEXTURE").ToArray()) texture.Remove();
        foreach(var element in resources.Descendants().Where(e => e.Name.LocalName is "DATABLOCK" or "SEGMENTSET" or "ROOTNODE").ToArray()) element.Remove();
        PortFiles.WritePssg(scene,landPath+".tmp"); ObjectVertexLayout.Verify(scene,PortFiles.ReadPssg(landPath+".tmp"));
        PortFiles.WritePssg(resources,path+".tmp");
        File.Move(landPath+".tmp",landPath,true); File.Move(path+".tmp",path,true);
        return new { TilesMoved=tiles, RouteTiles=routeTiles, TotalTiles=tiles+routeTiles, Meshes=scene.Descendants("RENDERDATASOURCE").Count(), TexturesRetained=resources.Descendants("TEXTURE").Count(), RuntimeValidated=false };
    }
}
