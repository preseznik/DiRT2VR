using System.Security.Principal;
using System.Text.Json;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.CustomCars;

public sealed record CarProgress(int Percent, string Message, bool CanCancel = true);
public static class CarConversion
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 4 || args[0] != "--convert-f2004") throw new IOException("Usage: DiRT2VR.exe --convert-f2004 <F2004 or Assetto Corsa folder> <DiRT 2 folder> <new output folder>");
            if (OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new IOException("Run the car converter normally, without Run as administrator.");
            Build(args[1], args[2], args[3], p => { Console.WriteLine(JsonSerializer.Serialize(p)); Console.Out.Flush(); }); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
    public static string SourceFolder(string path)
    {
        path = Path.GetFullPath(path);
        return File.Exists(Path.Combine(path, "ferrari_f2004.kn5")) ? path : SafeFiles.Inside(path, "content/cars/ks_ferrari_f2004");
    }
    public static void Build(string source, string donor, string output, Action<CarProgress>? progress = null, CancellationToken cancel = default)
    {
        source = SourceFolder(source); donor = Path.GetFullPath(donor); output = Path.GetFullPath(output);
        SafeFiles.NoLinks(source); SafeFiles.NoLinks(donor); SafeFiles.NoLinks(output);
        if (Directory.Exists(output) || File.Exists(output) || new[] { source, donor }.Any(p => output == p || output.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new IOException("Choose a new build directory outside either game.");
        var sources = CarPack.Sources();
        void VerifySources()
        {
            foreach (var file in sources)
                if (SafeFiles.Hash(SafeFiles.Inside(file.Game == "dirt2" ? donor : source, file.Path), cancel) != file.Sha256) throw new IOException("Unsupported or modified source file: " + file.Path);
            var known = sources.Where(f => f.Game == "assettocorsa").Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in SafeFiles.Tree(SafeFiles.Inside(source, "skins/00_official")).Where(f => Path.GetExtension(f).Equals(".dds", StringComparison.OrdinalIgnoreCase)))
                if (!known.Contains(Path.GetRelativePath(source, file).Replace('\\', '/'))) throw new IOException("The supported livery contains an additional texture. Use the original F2004 source.");
        }
        void Report(int percent, string message) { cancel.ThrowIfCancellationRequested(); progress?.Invoke(new(percent, message)); }
        Report(0, "Checking the supported Ferrari F2004 source and original DiRT 2 files"); VerifySources();
        Directory.CreateDirectory(output); string install = Path.Combine(output, "install"), exterior = Path.Combine(output, "exterior"), cockpit = Path.Combine(output, "cockpit"), handling = Path.Combine(output, "handling"), physics = Path.Combine(output, "physics");
        Report(10, "Converting body, wheels, suspension and detail levels"); FerrariExterior.Build(source, donor, exterior);
        WheelValidation.Write(Path.Combine(source, "ferrari_f2004.kn5"), exterior, Path.Combine(output, "wheel-validation.json"));
        Report(45, "Converting the cockpit, steering and reduced-shake camera"); FerrariInterior.Build(source, donor, cockpit);
        string driver=Path.Combine(output,"driver");
        Report(52, "Fitting the seated driver, steering grips and harness"); FerrariDriver.Build(source,donor,cockpit,driver);
        Report(60, "Reading source physics and building the handling approximation"); SourcePhysics.Extract(source, physics, cancel);
        string schema = Path.Combine(output, "handling-schema.xml");
        using (var resource = typeof(CarConversion).Assembly.GetManifestResourceStream("f2004-ctf-schema.xml")!)
        using (var target = File.Create(schema)) resource.CopyTo(target);
        FerrariHandling.Build(donor, schema, physics, handling);
        Report(70, "Building native interior and exterior V10 audio"); Directory.CreateDirectory(install); FerrariAudio.Build(source, donor, install, cancel);
        Report(85, "Preparing independent car resources");
        foreach (var file in sources.Where(f => f.Game == "dirt2" && (f.Path.StartsWith("cars/prc/", StringComparison.Ordinal) || f.Path.StartsWith("interiors/prc/", StringComparison.Ordinal))))
        {
            string relative = file.Path.Replace("prc", "f04", StringComparison.Ordinal); string target = SafeFiles.Inside(install, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); byte[] bytes = File.ReadAllBytes(SafeFiles.Inside(donor, file.Path));
            if (file.Path.StartsWith("interiors/", StringComparison.Ordinal) && Path.GetExtension(file.Path) == ".xml") bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("prc", "f04", StringComparison.Ordinal));
            File.WriteAllBytes(target, bytes);
        }
        void Overlay(string root, string name, string relative) { cancel.ThrowIfCancellationRequested(); string target = SafeFiles.Inside(install, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(root, name), target, true); }
        foreach (var name in new[] { "f04_highLOD.pssg", "f04_lowLOD.pssg", "f04.nd2", "f04_AO.pssg" }) Overlay(exterior, name, "cars/f04/" + name);
        foreach (var name in new[] { "f04.ctf", "f04_A.ctf", "f04_B.ctf" }) Overlay(handling, name, "cars/f04/" + name);
        foreach (var name in new[] { "cameras.xml", "f04_anim_dummies.pssg" }) Overlay(driver, name, "cars/f04/" + name);
        Overlay(cockpit, "int_f04.xml", "interiors/f04/int_f04.xml");
        foreach (var name in new[] { "int_f04.pssg", "int_f04_driver_anims.pssg", "int_f04_anims.pssg", "int_f04_anim.xml" }) Overlay(driver, name, "interiors/f04/" + name);
        Overlay(driver, "driver-build.json", CarPack.Support + "/driver.json");
        Overlay(exterior, "ambient-shadow-build.json", CarPack.Support + "/ambient-shadow.json");
        Report(95, "Verifying all generated files and preserving source integrity"); VerifySources();
        var files = SafeFiles.Tree(install).Order(StringComparer.Ordinal).Select(path => new PackFile(Path.GetRelativePath(install, path).Replace('\\', '/'), new FileInfo(path).Length, SafeFiles.Hash(path, cancel))).ToArray();
        var receipt = new CarReceipt(1, CarPack.Code, CarPack.Version, files, sources); CarPack.Verify(install, receipt, cancel);
        SafeFiles.WriteJson(SafeFiles.Inside(install, CarPack.Receipt), receipt);
        Report(100, "Ferrari F2004 built for experimental desktop solo testing");
    }
}
