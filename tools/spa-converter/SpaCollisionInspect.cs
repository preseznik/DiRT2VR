using System.Numerics;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class SpaCollisionInspect
{
    internal static Matrix4x4 World(PssgNode node)
    {
        var result=node.Transform.Transform;
        for(var p=node.ParentElement;p is not null;p=p.ParentElement)if(p is PssgNode n)result*=n.Transform.Transform;
        return result;
    }
    internal static IEnumerable<PssgNode> Nodes(PssgElement node)
    {
        if(node is PssgNode n)yield return n;
        foreach(var child in node.ChildElements)foreach(var item in Nodes(child))yield return item;
    }
    internal static QuadTreeDataTriangle[] Ground(string path)
    {
        using var stream=File.OpenRead(path);var archive=new JpkFile();archive.Read(stream);
        return TrackGround.Load(archive).TraverseGrid().SelectMany(c=>c.QuadTree.GetTriangles()).DistinctBy(GeometryCoverage.Exact).ToArray();
    }
    internal static void Dump(string path,IEnumerable<QuadTreeDataTriangle> triangles)
    {
        using var writer=new BinaryWriter(new FileStream(path,FileMode.CreateNew));
        foreach(var t in triangles){foreach(var p in new[]{t.Position0,t.Position1,t.Position2}){writer.Write(p.X);writer.Write(p.Y);writer.Write(p.Z);}writer.Write(System.Text.Encoding.ASCII.GetBytes(t.Material));}
    }
}
