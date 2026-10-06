using System.Numerics;
using EgoEngineLibrary.Graphics.Pssg.Elements;

namespace DiRT2VR.Nordschleife;

internal static class StartingGrid
{
    internal const int Capacity = 8;
    internal const float ApproachMetres = 40;

    internal static Matrix4x4[] Positions(Scene scene)
    {
        var start = scene.Gates[0];
        var tangent = Vector3.Normalize(new Vector3(start.Tangent.X, 0, start.Tangent.Z));
        var left = new Vector3(tangent.Z, 0, -tangent.X);
        var right = -left;
        if (scene.FullCourse && (start.Left < 1.5f || start.Right < 4.7f))
            throw new InvalidDataException("The starting road is too narrow for an eight-car grid.");
        return Enumerable.Range(0, scene.FullCourse ? Capacity : 1).Select(i =>
        {
            // Keep the accepted solo position; a parallel right-hand column stays
            // on asphalt where the road curves behind the timing line.
            float lateral = scene.FullCourse && i % 2 != 0 ? -3.2f : 0;
            var position = start.Position - tangent * (10 + i / 2 * 6) + left * lateral;
            position.Y = scene.RoadHeight(position) + .6f;
            // Verify the car footprint has original asphalt beneath it, not just its centre.
            if (scene.FullCourse)
                foreach (int front in new[] { -1, 1 })
                    foreach (int side in new[] { -1, 1 })
                        scene.RoadHeight(position + tangent * (front * 2.5f) + left * (side * 1.2f));
            return new Matrix4x4(right.X, right.Y, right.Z, 0, 0, 1, 0, 0,
                -tangent.X, -tangent.Y, -tangent.Z, 0, position.X, position.Y, position.Z, 1);
        }).ToArray();
    }

    internal static void Write(Scene scene, string donor, string output)
    {
        var grid = Files.Pssg(Path.Combine(donor, "route_1/grids.pssg"));
        var positions = Positions(scene);
        var standing = grid.Elements<PssgNode>().Where(n => n.Id.Length == 7 && n.Id.StartsWith("slot_", StringComparison.Ordinal)).ToArray();
        if (standing.Length != Capacity || Enumerable.Range(0, Capacity).Any(i => standing.All(n => n.Id != $"slot_{i:00}")))
            throw new InvalidDataException("Unsupported donor standing grid.");
        foreach (var node in grid.Elements<PssgNode>())
        {
            node.Transform.Transform = Matrix4x4.Identity;
            if (!node.Id.StartsWith("slot_", StringComparison.Ordinal)) continue;
            if (node.Id.Length < 7 || !int.TryParse(node.Id.AsSpan(5, 2), out int index) || index is < 0 or >= Capacity)
                throw new InvalidDataException("Unsupported donor grid slot: " + node.Id);
            node.Transform.Transform = positions[scene.FullCourse ? index : 0];
        }
        Files.Save(grid, Path.Combine(output, "grids.pssg"));
        Files.Json(Path.Combine(output, "grid.json"), new
        {
            Capacity = positions.Length, ClearanceMetres = .6f, RowSpacingMetres = 6, LaneSpacingMetres = 3.2f,
            Slots = positions.Select((p, i) => new { Id = $"slot_{i:00}", Position = new[] { p.M41, p.M42, p.M43 } }),
            OriginalRoadFootprintsVerified = scene.FullCourse, RuntimeValidated = false
        });
    }
}
