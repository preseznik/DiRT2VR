using System.Numerics;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class SpaGrid
{
    internal const int Capacity = 8;
    internal const float Clearance = .6f;

    internal static Matrix4x4[] Positions(PssgFile source, Vector3 origin, Func<Vector3, float> roadHeight)
    {
        var root = source.Elements<PssgNode>().Single(n => n.Id == "grid_start_standing_0");
        var slots = root.ChildElements.OfType<PssgNode>().ToDictionary(n => n.Id, StringComparer.Ordinal);
        return Enumerable.Range(0, Capacity).Select(i =>
        {
            if (!slots.TryGetValue($"slot_{i:00}", out var node))
                throw new InvalidDataException("Missing source Spa grid position: " + i);
            // Slot positions are local to the source standing-grid parent.
            var matrix = SpaCollisionInspect.World(node);
            var right = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            var back = new Vector3(matrix.M31, matrix.M32, matrix.M33);
            var position = matrix.Translation - origin;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Z) || !float.IsFinite(matrix.GetDeterminant()) ||
                MathF.Abs(right.Length() - 1) > .001f || MathF.Abs(back.Length() - 1) > .001f ||
                MathF.Abs(Vector3.Dot(right, back)) > .001f || MathF.Abs(matrix.GetDeterminant() - 1) > .001f)
                throw new InvalidDataException("Invalid source Spa grid transform: " + i);
            // Source standing slots have placeholder Y=0. Project their original
            // X/Z onto the road without moving the grid sideways or along the lap.
            position.Y = roadHeight(position) + Clearance;
            if (!float.IsFinite(position.Y)) throw new InvalidDataException("Invalid Spa grid road height.");
            foreach (int front in new[] { -1, 1 })
                foreach (int side in new[] { -1, 1 })
                {
                    float height = roadHeight(position + back * (front * 2.5f) + right * (side * 1.2f));
                    if (!float.IsFinite(height) || MathF.Abs(height - (position.Y - Clearance)) > .5f)
                        throw new InvalidDataException("Spa starting footprint is not supported by the road: " + i);
                }
            matrix.Translation = position;
            return matrix;
        }).ToArray();
    }

    internal static void Apply(PssgFile grid, Matrix4x4[] positions)
    {
        var nodes = grid.Elements<PssgNode>().ToArray();
        var standing = nodes.Where(n => n.Id.Length == 7 && n.Id.StartsWith("slot_", StringComparison.Ordinal)).ToArray();
        if (positions.Length != Capacity || standing.Length != Capacity ||
            Enumerable.Range(0, Capacity).Any(i => standing.All(n => n.Id != $"slot_{i:00}")))
            throw new InvalidDataException("Unsupported native Spa standing grid.");
        foreach (var node in nodes)
        {
            // Keep the donor's native grid identities, with world-space matrices.
            node.Transform.Transform = Matrix4x4.Identity;
            if (!node.Id.StartsWith("slot_", StringComparison.Ordinal)) continue;
            if (node.Id.Length < 7 || !int.TryParse(node.Id.AsSpan(5, 2), out int index) || index is < 0 or >= Capacity)
                throw new InvalidDataException("Unsupported native Spa grid slot: " + node.Id);
            node.Transform.Transform = positions[index];
        }
    }

    internal static void Write(Scene scene, string source, string donor, string output)
    {
        string path = Files.Inside(source, "tracks/locations/europe/spa_francorchamps/route_0/grids.pssg");
        scene.Inputs[path] = Files.Hash(path);
        float Road(Vector3 p)
        {
            // Seed the height query from the nearby progress gate, not the source
            // grid's placeholder vertical coordinate; RoadHeight checks within 4 m.
            p.Y = scene.Gates.MinBy(g => (g.Position.X-p.X)*(g.Position.X-p.X) + (g.Position.Z-p.Z)*(g.Position.Z-p.Z))!.Position.Y;
            return scene.RoadHeight(p);
        }
        var positions = Positions(Files.Pssg(path), scene.Origin, Road);
        var grid = Files.Pssg(Path.Combine(donor, "route_1/grids.pssg"));
        Apply(grid, positions);
        Files.Save(grid, Path.Combine(output, "grids.pssg"));
        Files.Json(Path.Combine(output, "grid.json"), new
        {
            Capacity, ClearanceMetres = Clearance, SourceStandingGrid = "grid_start_standing_0",
            SourcePositionsAndHeadingsRetained = true, OriginalRoadFootprintsVerified = true,
            Slots = positions.Select((p, i) => new { Id = $"slot_{i:00}", Position = new[] { p.M41, p.M42, p.M43 } }),
            RuntimeValidated = false
        });
    }
}
