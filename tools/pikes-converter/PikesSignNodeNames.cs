using System.Text.Json;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static partial class PikesRoadObjects
{
    static PssgNode[] SignChildNodes(PssgFile file,int? expectedModels=21)
    {
        var roots=file.Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE" && n.Id.StartsWith("pikes40_",StringComparison.Ordinal)).ToArray();
        if(roots.Length<1||(expectedModels.HasValue&&roots.Length!=expectedModels.Value))throw new InvalidDataException("Unexpected sign model roots: "+roots.Length);
        foreach(var root in roots)
            if(root.ChildElements.OfType<PssgNode>().Count(n=>n.Name=="LODVISIBLERENDERNODE")!=1)
                throw new InvalidDataException("Expected one sign LOD: "+root.Id);
        var nodes=roots.SelectMany(r=>Below(r).OfType<PssgNode>().Skip(1)).ToArray();
        if(nodes.Length!=roots.Length*7)throw new InvalidDataException("Unexpected sign node hierarchy");
        return nodes;
    }

    static string SignNodeName(PssgNode node)=>node.Name=="LODVISIBLERENDERNODE"?"lod":node.Id;

    static void ValidSignNodeNames(PssgFile file,int? expectedModels=21)
    {
        var invalid=SignChildNodes(file,expectedModels).Where(n=>!n.Attributes.Any(a=>a.Name=="nickname" && a.DisplayValue==SignNodeName(n))).ToArray();
        if(invalid.Length!=0)
            throw new InvalidDataException($"Missing or incorrect native sign node nicknames ({invalid.Length}): {invalid[0].Id}");
    }
}
