using System.Security.Principal;
using System.Text.Json;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.Aspen;

// Uses the same bundled runtime as Aspen, in a separate non-elevated process.
public static class SmelterConversion
{
    public static ConversionProfile GetProfile()
    {
        using var stream = typeof(SmelterConversion).Assembly.GetManifestResourceStream("smelter-sources.json")!;
        var sources = JsonSerializer.Deserialize<Fingerprint[]>(stream)!;
        TrackPack.ValidateSources(sources);
        var pack = TrackPacks.Smelter;
        return new(pack.Id, pack.Name, pack.Version, pack.MinimumLauncher, 4L << 30, 800L << 20,
            pack.Modes.ToArray(), pack.Layouts.ToArray(), sources);
    }
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 4 || args[0] != "--convert-smelter" || args.Skip(1).Any(string.IsNullOrWhiteSpace))
                throw new IOException("Usage: DiRT2VR.exe --convert-smelter <DiRT 3 folder> <DiRT 2 folder> <new output folder>");
            if (OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new IOException("Start the track converter normally, without Run as administrator.");
            Build(args[1], args[2], args[3], value => { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); });
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
    public static void Build(string d3, string d2, string output, Action<TrackProgress>? progress)
    {
        SafeFiles.RequireClosed();
        d3 = Path.GetFullPath(d3); d2 = Path.GetFullPath(d2); output = Path.GetFullPath(output);
        PortFiles.NewOutput(output, d3, d2);
        var profile = GetProfile(); var pack = TrackPacks.Smelter; var layout = pack.Layouts.Single();
        void Report(int percent, string message) => progress?.Invoke(new(percent, message));
        Report(0, "Checking your DiRT 2 and DiRT 3 source files");
        TrackPack.VerifySources(profile, d2, d3, null, default);
        var expected = profile.Sources.ToDictionary(f => Path.GetFullPath(Path.Combine(f.Game == "dirt3" ? d3 : d2, f.Path)), f => f.Sha256, StringComparer.OrdinalIgnoreCase);
        ShaderTemplates.Donors = profile.Sources.Where(f => f.Game == "dirt2" && f.Path.StartsWith("tracks/")).Select(f => f.Path).ToArray();
        string schemaPath = Path.Combine(output, "schemaDirt2.xml");
        using (var schema = typeof(SmelterConversion).Assembly.GetManifestResourceStream("schemaDirt2.xml")!)
        using (var target = File.Create(schemaPath)) schema.CopyTo(target);
        EgoEngineLibrary.Graphics.Pssg.PssgSchema.ResetSchema();
        var candidate = Path.Combine(output, "build");
        Report(10, "Building County Loop — Morning sun");
        SmelterBuild.Run(d3, d2, schemaPath, candidate, message => Report(30, message));
        foreach (var input in SafeFiles.ReadJson<Dictionary<string, string>>(Path.Combine(candidate, "inputs.json")).Where(p => p.Key != schemaPath))
            if (!expected.TryGetValue(Path.GetFullPath(input.Key), out var hash) || hash != input.Value)
                throw new IOException("Conversion used an unsupported source file: " + input.Key);
        var files = new List<PackFile>(); var session = new List<SessionFile>();
        var install = Path.Combine(output, "install");
        Report(90, "Preparing County Loop installation files");
        foreach (var source in SafeFiles.Tree(Path.Combine(candidate, "track")))
            Copy(source, "tracks/usa/" + layout.Folder + "/" + Path.GetRelativePath(Path.Combine(candidate, "track"), source).Replace('\\', '/'));
        AddSession("surface_materials.xml", Path.Combine(candidate, "surface_materials.xml"));
        AddSession("tracks/waterdefs.xml", Path.Combine(candidate, "waterdefs.xml"));
        using (var metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(candidate, "session-metadata/metadata.json"))))
            foreach (var entry in metadata.RootElement.GetProperty("Files").EnumerateArray())
            {
                var path = entry.GetProperty("Path").GetString()!;
                AddSession(path, SafeFiles.Inside(Path.Combine(candidate, "session-metadata"), path));
            }
        Report(95, "Verifying County Loop and original source files");
        TrackPack.VerifySources(profile, d2, d3, null, default);
        var receipt = new PackReceipt(1, pack.Id, pack.Version, pack.MinimumLauncher, files.ToArray(), [new(layout.Id, session.ToArray())], profile.Sources);
        pack.Verify(install, receipt);
        SafeFiles.WriteJson(SafeFiles.Inside(install, pack.Receipt), receipt);
        Report(100, "County Loop is ready to install for desktop testing");

        void Copy(string source, string relative)
        {
            var target = SafeFiles.Inside(install, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            PortFiles.CopyNew(source, target);
            files.Add(new(relative, new FileInfo(target).Length, SafeFiles.Hash(target)));
        }
        void AddSession(string target, string source)
        {
            var owned = pack.Support + "/" + layout.Id + "/" + target;
            Copy(source, owned);
            session.Add(new(target, expected[Path.GetFullPath(Path.Combine(d2, target))], owned));
        }
    }
}
