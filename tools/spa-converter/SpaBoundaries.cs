using System.Numerics;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;

internal static class SpaBoundaries
{
    internal static void Write(Scene scene,string source,string output)
    {
        string path=Files.Inside(source,"tracks/locations/europe/spa_francorchamps/route_0/boundarylines.cqtc");scene.Inputs[path]=Files.Hash(path);
        var original=new CQuadTreeFile(File.ReadAllBytes(path)).GetTriangles().DistinctBy(GeometryCoverage.Exact).ToArray();
        if(original.Length==0||original.Any(t=>t.Material!="FBND"))throw new InvalidDataException("Unexpected Spa physical boundary");
        var expected=original.Select(t=>new QuadTreeDataTriangle(t.Position0-scene.Origin,t.Position1-scene.Origin,t.Position2-scene.Origin,t.Material)).ToArray();
        var builder=new QuadTreeMeshDataBuilder(CQuadTreeTypeInfo.Get(CQuadTreeType.Dirt));foreach(var t in expected)builder.Add(t);
        var target=Path.Combine(output,"boundarylines.cqtc");File.WriteAllBytes(target,CQuadTreeFile.Create(builder.Build()).Bytes);
        var saved=new CQuadTreeFile(File.ReadAllBytes(target)).GetTriangles();
        int missing=GeometryCoverage.Unmatched(expected,saved),extra=GeometryCoverage.Unmatched(saved,expected);
        Files.Json(Path.Combine(output,"source-boundaries.json"),new{Passed=missing==0&&extra==0,SourceFaces=expected.Length,DecodedFaces=saved.Length,Missing=missing,Extra=extra,TranslationOnly=true,SourceMaterialAndWindingRetained=true,GeneratedOffsetWallsRemoved=true,RuntimeValidated=false});
        if(missing!=0||extra!=0)throw new InvalidDataException("Authored barrier collision changed on readback");
    }
}
