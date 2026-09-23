using System.Text.Json;
using EgoEngineLibrary.Archive.Jpk;
using EgoEngineLibrary.Formats.TrackQuadTree;
using EgoEngineLibrary.Formats.TrackQuadTree.Static;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Xml;

if (args.Length == 2 && args[0] == "mask-test")
{
    VisibilityMasks.Test(args[1]);
    return 0;
}

if (args.Length == 3 && args[0] == "pssg-xml")
{
    using var input = File.OpenRead(args[1]);
    using var outputXml = new FileStream(args[2], FileMode.CreateNew);
    PssgFile.Open(input).WriteXml(outputXml);
    return 0;
}

if (args.Length == 3 && args[0] == "visual-check")
    return VisualCheck.Run(args[1], args[2]);

if (args.Length == 4 && args[0] == "clearance")
{
    ClearanceCheck.Run(args[1],args[2],args[3]);
    return 0;
}
if (args.Length == 4 && args[0] == "author")
{
    TrackAuthor.Build(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Path.GetFullPath(args[3]));
    return 0;
}
if (args.Length != 3 || args[0] is not ("inspect" or "roundtrip"))
{
    Console.Error.WriteLine("TrackPrototype inspect|roundtrip <route directory> <new output directory>");
    return 2;
}
var source = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
if (output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new IOException("Output cannot be inside the source route.");
Directory.CreateDirectory(output);
var reports = new List<object>();
foreach (var name in new[] { "ai_track.xml", "progress_track.xml", "route_overrides.xml", "ai_vehicle_track.xml" })
{
    using var input = File.OpenRead(Path.Combine(source, name));
    var xml = new XmlFile(input);
    using (var text = File.Create(Path.Combine(output, name + ".decoded.xml"))) xml.Write(text, XmlType.Text);
    if (args[0] == "roundtrip")
    {
        using (var file = File.Create(Path.Combine(output, name))) xml.Write(file);
        using var file2 = File.OpenRead(Path.Combine(output, name));
        var again = new XmlFile(file2);
        if (xml.Document.OuterXml != again.Document.OuterXml) throw new InvalidDataException(name + " XML changed on round trip.");
    }
    reports.Add(new { File = name, Result = args[0] == "inspect" ? "decoded" : "semantic roundtrip passed" });
}
foreach (var name in new[] { "routesplit.pssg", "grids.pssg" })
{
    using var input = File.OpenRead(Path.Combine(source, name));
    var pssg = PssgFile.Open(input);
    using (var text = File.Create(Path.Combine(output, name + ".xml"))) pssg.WriteXml(text);
    if (args[0] == "roundtrip")
    {
        using (var file = File.Create(Path.Combine(output, name))) pssg.Save(file);
        using var file2 = File.OpenRead(Path.Combine(output, name));
        var again = PssgFile.Open(file2);
        using var before = new MemoryStream();
        using var after = new MemoryStream();
        pssg.WriteXml(before); again.WriteXml(after);
        if (!before.ToArray().AsSpan().SequenceEqual(after.ToArray())) throw new InvalidDataException(name + " PSSG changed on round trip.");
    }
    reports.Add(new { File = name, Result = args[0] == "inspect" ? "decoded" : "semantic roundtrip passed" });
}
using (var input = File.OpenRead(Path.Combine(source, "track.jpk")))
{
    var archive = new JpkFile(); archive.Read(input);
    var ground = TrackGround.Load(archive);
    var gltf = TrackGroundGltfConverter.Convert(ground);
    gltf.SaveGLB(Path.Combine(output, "ground.glb"));
    if (args[0] == "roundtrip")
    {
        var rebuilt = GltfTrackGroundConverter.Convert(gltf, VcQuadTreeTypeInfo.Get(VcQuadTreeType.Dirt2));
        using (var file = File.Create(Path.Combine(output, "track.jpk"))) rebuilt.Save().Write(file);
        using var check = File.OpenRead(Path.Combine(output, "track.jpk"));
        var again = new JpkFile(); again.Read(check);
        var readback = TrackGroundGltfConverter.Convert(TrackGround.Load(again));
        readback.SaveGLB(Path.Combine(output, "ground-readback.glb"));
        reports.Add(new { File = "track.jpk", Result = "rebuilt and decoded; geometry comparison required", InputEntries = archive.Entries.Count, OutputEntries = again.Entries.Count });
    }
}
foreach (var name in new[] { "boundarylines.cqtc", "resetlines.cqtc", "cameralines.cqtc" })
{
    var tree = new CQuadTreeFile(File.ReadAllBytes(Path.Combine(source, name)));
    var gltf = TrackGroundGltfConverter.Convert(tree);
    gltf.SaveGLB(Path.Combine(output, name + ".glb"));
    if (args[0] == "roundtrip")
    {
        var rebuilt = GltfTrackGroundConverter.Convert(gltf, CQuadTreeTypeInfo.Get(CQuadTreeType.Dirt));
        File.WriteAllBytes(Path.Combine(output, name), rebuilt.Bytes);
        TrackGroundGltfConverter.Convert(new CQuadTreeFile(rebuilt.Bytes)).SaveGLB(Path.Combine(output, name + "-readback.glb"));
        reports.Add(new { File = name, Result = "rebuilt and decoded; geometry comparison required" });
    }
}
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
if (args[0] == "roundtrip") GeometryCheck.Verify(output);
Console.WriteLine($"{args[0]} complete: {output}");
return 0;
