using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using DiRT2VR.CustomTracks;

try
{
    if (args.Length != 4 || args[0] != "build") throw new IOException("Usage: AspenConverter build <DiRT 3 folder> <DiRT 2 folder> <new staging folder>");
    if (OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        throw new IOException("Start the track converter normally, without Run as administrator.");
    SafeFiles.RequireClosed();
    string d3 = Path.GetFullPath(args[1]), d2 = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
    PortFiles.NewOutput(output, d3, d2);
    using var sourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("sources.json")!;
    var sources = JsonSerializer.Deserialize<Fingerprint[]>(sourceStream)!;
    AspenPack.ValidateSources(sources);
    var expected = sources.ToDictionary(f => Path.GetFullPath(Path.Combine(f.Game == "dirt3" ? d3 : d2, f.Path)), f => f.Sha256, StringComparer.OrdinalIgnoreCase);
    Report(0, "Checking your DiRT 2 and DiRT 3 source files");
    foreach (var source in expected)
        if (!File.Exists(source.Key) || SafeFiles.Hash(source.Key) != source.Value)
            throw new IOException("Missing or unsupported source file: " + source.Key + ". Verify the original game files in Steam, or choose another installation.");
    ShaderTemplates.Donors = sources.Where(f => f.Game == "dirt2" && f.Path.StartsWith("tracks/")).Select(f => f.Path).ToArray();
    PortBuild.SourceFiles = expected.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    PortBuild.SchemaPath = Path.Combine(output, "schemaDirt2.xml");
    using (var schema = Assembly.GetExecutingAssembly().GetManifestResourceStream("schemaDirt2.xml")!)
    using (var target = File.Create(PortBuild.SchemaPath)) schema.CopyTo(target);
    var files = new List<PackFile>(); var sessions = new List<LayoutSession>();
    string install = Path.Combine(output, "install");
    for (int i = 0; i < AspenPack.Layouts.Length; i++)
    {
        SafeFiles.RequireClosed();
        var layout = AspenPack.Layouts[i];
        int percent = 5 + i * 22;
        Report(percent, "Building " + layout.Name + " — " + layout.Condition);
        PortBuild.Progress = message => Report(percent, layout.Name + ": " + message);
        var candidate = Path.Combine(output, "build", layout.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
        // Staff conversion adds animation schema entries. Each layout starts with
        // a fresh schema, just like the individually accepted laboratory builds.
        EgoEngineLibrary.Graphics.Pssg.PssgSchema.ResetSchema();
        Entities.RegisterSchema();
        PortBuild.Run(d3, d2, candidate, AspenLayout.Parse(layout.Id[6..]));
        var inputs = SafeFiles.ReadJson<Dictionary<string, string>>(Path.Combine(candidate, "inputs.json"));
        foreach (var input in inputs.Where(p => p.Key != PortBuild.SchemaPath))
            if (!expected.TryGetValue(Path.GetFullPath(input.Key), out var hash) || hash != input.Value)
                throw new IOException("Conversion used an unsupported source file: " + input.Key);
        foreach (var source in SafeFiles.Tree(Path.Combine(candidate, "track")))
            Copy(source, "tracks/usa/" + layout.Folder + "/" + Path.GetRelativePath(Path.Combine(candidate, "track"), source).Replace('\\', '/'));
        var session = new List<SessionFile>();
        AddSession("surface_materials.xml", Path.Combine(candidate, "surface_materials.xml"));
        foreach (var name in new[] { "session-metadata", "session-effects" })
        {
            string manifest = Path.Combine(candidate, name, name == "session-metadata" ? "metadata.json" : "effects.json");
            using var data = JsonDocument.Parse(File.ReadAllBytes(manifest));
            foreach (var entry in data.RootElement.GetProperty("Files").EnumerateArray())
            {
                string path = entry.GetProperty("Path").GetString()!;
                AddSession(path, SafeFiles.Inside(Path.Combine(candidate, name), path));
            }
        }
        sessions.Add(new(layout.Id, session.ToArray()));
        void AddSession(string target, string source)
        {
            string owned = AspenPack.Support + "/" + layout.Id + "/" + target;
            Copy(source, owned);
            session.Add(new(target, expected[Path.GetFullPath(Path.Combine(d2, target))], owned));
        }
    }
    Report(95, "Verifying all four layouts and original source files");
    foreach (var source in expected)
        if (SafeFiles.Hash(source.Key) != source.Value) throw new IOException("Source changed during conversion: " + source.Key);
    var receipt = new PackReceipt(1, AspenPack.Id, AspenPack.Version, AspenPack.MinimumLauncher, files.ToArray(), sessions.ToArray(), sources);
    AspenPack.Verify(install, receipt);
    SafeFiles.WriteJson(SafeFiles.Inside(install, AspenPack.Receipt), receipt);
    Report(100, "All four layouts are ready to install");
    return 0;
    void Copy(string source, string relative)
    {
        var target = SafeFiles.Inside(install, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, false);
        files.Add(new(relative, new FileInfo(target).Length, SafeFiles.Hash(target)));
    }
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static void Report(int percent, string message) => Console.WriteLine(JsonSerializer.Serialize(new { Percent = percent, Message = message }));
