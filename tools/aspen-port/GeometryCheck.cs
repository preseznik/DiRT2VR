using System.Numerics;
using System.Text.Json;
using SharpGLTF.Runtime;
using SharpGLTF.Schema2;

internal static class GeometryCheck
{
    internal record Triangle(Vector3 A, Vector3 B, Vector3 C, string Material);
    internal static List<Triangle> Read(string path)
    {
        var model = ModelRoot.Load(path);
        var scene = SceneTemplate.Create(model.DefaultScene, new RuntimeOptions { IsolateMemory = false }).CreateInstance();
        scene.Armature.SetPoseTransforms();
        var result = new List<Triangle>();
        foreach (var drawable in scene)
        foreach (var p in model.LogicalMeshes[drawable.Template.LogicalMeshIndex].Decode().Primitives)
        foreach (var (a, b, c) in p.TriangleIndices)
            result.Add(new(p.GetPosition(a, drawable.Transform), p.GetPosition(b, drawable.Transform), p.GetPosition(c, drawable.Transform), p.Material.Name));
        return result;
    }
    static (int, int, int) Cell(Vector3 p) => ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
    static Vector3 Center(Triangle t) => (t.A + t.B + t.C) / 3;
    static bool Same(Triangle a, Triangle b)
    {
        const float maxSquared = 0.02f * 0.02f;
        bool Near(Vector3 x, Vector3 y) => Vector3.DistanceSquared(x, y) <= maxSquared;
        return a.Material == b.Material &&
            ((Near(a.A,b.A) && Near(a.B,b.B) && Near(a.C,b.C)) ||
             (Near(a.A,b.B) && Near(a.B,b.C) && Near(a.C,b.A)) ||
             (Near(a.A,b.C) && Near(a.B,b.A) && Near(a.C,b.B)));
    }
    internal static int Unmatched(List<Triangle> source, List<Triangle> target)
    {
        var index = target.GroupBy(t => Cell(Center(t))).ToDictionary(g => g.Key, g => g.ToArray());
        int missing = 0;
        foreach (var triangle in source)
        {
            var (x,y,z) = Cell(Center(triangle)); bool found = false;
            for (int dx=-1;dx<=1 && !found;dx++)
            for (int dy=-1;dy<=1 && !found;dy++)
            for (int dz=-1;dz<=1 && !found;dz++)
                if (index.TryGetValue((x+dx,y+dy,z+dz), out var candidates)) found = candidates.Any(t => Same(triangle,t));
            if (!found) missing++;
        }
        return missing;
    }
    internal static void Verify(string directory)
    {
        var results = new List<object>(); bool passed = true;
        foreach (var name in new[] { "ground", "boundarylines.cqtc", "resetlines.cqtc", "cameralines.cqtc" })
        {
            var before = Read(Path.Combine(directory, name + ".glb"));
            var after = Read(Path.Combine(directory, name + "-readback.glb"));
            var lost = Unmatched(before,after); var added = Unmatched(after,before);
            passed &= lost == 0 && added == 0;
            results.Add(new { File=name, InputTriangles=before.Count, OutputTriangles=after.Count, Missing=lost, Added=added, ToleranceMetres=0.02, SurfaceAndWindingChecked=true });
        }
        File.WriteAllText(Path.Combine(directory,"geometry-check.json"), JsonSerializer.Serialize(new { Passed=passed, Results=results }, new JsonSerializerOptions { WriteIndented=true }));
        if (!passed) throw new InvalidDataException("Round-trip geometry mismatch; see geometry-check.json.");
    }
}
