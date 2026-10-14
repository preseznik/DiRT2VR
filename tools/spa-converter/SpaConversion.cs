using System.Security.Principal;
using System.Text.Json;
using DiRT2VR.Nordschleife;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Data;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Spa;

public static class SpaConversion
{
    public static ConversionProfile GetProfile()
    {
        using var stream=typeof(SpaConversion).Assembly.GetManifestResourceStream("spa-sources.json")!;
        var sources=JsonSerializer.Deserialize<Fingerprint[]>(stream)!;
        TrackPack.ValidateSources(sources,"grid2");
        var pack=TrackPacks.Spa;
        return new(pack.Id,pack.Name,pack.Version,pack.MinimumLauncher,8L<<30,2L<<30,pack.Modes.ToArray(),pack.Layouts.ToArray(),sources);
    }
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length!=4||args[0]!="--convert-spa"||args.Skip(1).Any(string.IsNullOrWhiteSpace))
                throw new IOException("Usage: DiRT2VR.exe --convert-spa <GRID 2 with Spa DLC folder> <DiRT 2 folder> <new output folder>");
            if(OperatingSystem.IsWindows()&&new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new IOException("Start the track converter normally, without Run as administrator.");
            Build(args[1],args[2],args[3],p=>{Console.WriteLine(JsonSerializer.Serialize(p));Console.Out.Flush();});
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
    public static void Build(string grid2,string d2,string output,Action<TrackProgress>? progress)
    {
        SafeFiles.RequireClosed();grid2=Path.GetFullPath(grid2);d2=Path.GetFullPath(d2);output=Path.GetFullPath(output);
        Files.NewOutput(output,grid2,d2);
        var profile=GetProfile();var pack=TrackPacks.Spa;
        void Report(int percent,string message)=>progress?.Invoke(new(percent,message));
        Report(0,"Checking your GRID 2 and DiRT 2 source files");
        var sources=TrackPack.VerifySources(profile,d2,grid2,null,default);
        var expected=sources.ToDictionary(f=>Path.GetFullPath(Path.Combine(f.Game=="dirt2"?d2:grid2,f.Path)),f=>f.Sha256,StringComparer.OrdinalIgnoreCase);
        string schema=Path.Combine(output,"schemaDirt2.xml");
        using(var input=typeof(Aspen.AspenConversion).Assembly.GetManifestResourceStream("schemaDirt2.xml")!)
        using(var target=File.Create(schema))input.CopyTo(target);
        var database=new DatabaseFile(Path.Combine(d2,"database/database.bin"),schema);
        int donorModel=(int)database.Tables["track_model"]!.Rows.Cast<System.Data.DataRow>().Single(r=>(string)r["file_string"]=="battersea"&&(string)r["route_string"]=="route_1")["id"];
        string candidate=Path.Combine(output,"build");
        string extracted=Path.Combine(output,"source");
        Report(5,"Extracting your GRID 2 Spa DLC");
        SpaArchive.Extract(SafeFiles.Inside(grid2,SpaArchive.RelativePath),extracted);
        Report(15,"Building Spa scenery, road and collision");
        SpaBuild.Run(extracted,d2,candidate);
        string details=Path.Combine(output,"details"),presentation=Path.Combine(output,"presentation");
        Report(55,"Adding grass, crowds and trackside markers");
        SpaDetails.Run(extracted,d2,candidate,details);
        Report(65,"Preparing timing sectors and replay cameras");
        SpaPresentation.Run(extracted,details,d2,presentation);
        foreach(var (path,hash) in SafeFiles.ReadJson<Dictionary<string,string>>(Path.Combine(candidate,"inputs.json")))
            if(!Path.GetFullPath(path).StartsWith(extracted+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && (!expected.TryGetValue(Path.GetFullPath(path),out var pin)||pin!=hash))throw new IOException("Conversion used an unsupported source file: "+path);
        using var segment=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(candidate,"segment.json")));
        candidate=presentation;
        using var validation=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(candidate,"validation.json")));
        float length=segment.RootElement.GetProperty("LengthMetres").GetSingle();
        var files=new List<PackFile>();var sessions=new List<LayoutSession>();string install=Path.Combine(output,"install");
        foreach(var layout in pack.Layouts)
        {
            SafeFiles.RequireClosed();Report(75,"Preparing "+layout.Condition+" and its recovery files");
            string variant=Path.Combine(output,layout.Id);Directory.CreateDirectory(variant);
            var assets=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            const string donor="tracks/london/battersea/";
            foreach(var f in profile.Sources.Where(f=>f.Game=="dirt2"&&f.Path.StartsWith(donor,StringComparison.Ordinal)))
            {
                string relative=f.Path[donor.Length..];
                if(relative.StartsWith("route_1/",StringComparison.Ordinal))
                {
                    relative=relative[8..];if(DonorScenery.OmittedRouteFiles.Contains(relative))continue;
                    assets["route_0/"+relative]=SafeFiles.Inside(d2,f.Path);
                }
                else if(!relative.Contains('/'))assets[relative]=SafeFiles.Inside(d2,f.Path);
            }
            foreach(var f in validation.RootElement.GetProperty("Files").EnumerateObject())
            {
                if(f.Name == "shaderpack.pssg")continue;
                string source=SafeFiles.Inside(candidate,f.Name);
                if(SafeFiles.Hash(source)!=f.Value.GetString())throw new IOException("Candidate changed while preparing the pack: "+f.Name);
                assets[f.Name.StartsWith("shared/",StringComparison.Ordinal)?f.Name[7..]:"route_0/"+f.Name]=source;
            }
            foreach(var (relative,source) in assets)Copy(source,"tracks/usa/"+layout.Folder+"/"+relative);
            string metadata=Path.Combine(variant,"metadata");
            TrackMetadata.Create(d2,schema,metadata,new(layout.Folder,"sparoute","SPA-FRANCORCHAMPS", "spa","SPA",donorModel),length,false);
            var session=new List<SessionFile>();
            using(var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(metadata,"metadata.json"))))
                foreach(var entry in manifest.RootElement.GetProperty("Files").EnumerateArray())
                {
                    string path=entry.GetProperty("Path").GetString()!;string owned=pack.Support+"/"+layout.Id+"/"+path;
                    Copy(SafeFiles.Inside(metadata,path),owned);
                    session.Add(new(path,expected[Path.GetFullPath(Path.Combine(d2,path))],owned));
                }
            string shaders="dx11/shaderpack/shaderpack.pssg",shaderSource=SafeFiles.Inside(candidate,"shaderpack.pssg");
            if(SafeFiles.Hash(shaderSource)!=validation.RootElement.GetProperty("Files").GetProperty("shaderpack.pssg").GetString())throw new IOException("Spa shader data changed during conversion.");
            string shaderOwned=pack.Support+"/"+layout.Id+"/"+shaders;Copy(shaderSource,shaderOwned);
            session.Add(new(shaders,expected[Path.GetFullPath(Path.Combine(d2,shaders))],shaderOwned));
            sessions.Add(new(layout.Id,session.ToArray()));
        }
        Report(95,"Verifying every installed file and original source");
        if(!sources.SequenceEqual(TrackPack.VerifySources(profile,d2,grid2,null,default)))
            throw new IOException("Source files changed during conversion. Retry with the original files unchanged.");
        var receipt=new PackReceipt(1,pack.Id,pack.Version,pack.MinimumLauncher,files.ToArray(),sessions.ToArray(),sources);
        pack.Verify(install,receipt);SafeFiles.WriteJson(SafeFiles.Inside(install,pack.Receipt),receipt);
        Report(100,"Spa-Francorchamps is ready for desktop Direct practice");
        void Copy(string source,string relative)
        {
            string target=SafeFiles.Inside(install,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            SafeFiles.NoLinks(source);using(var input=File.OpenRead(source))using(var stream=new FileStream(target,FileMode.CreateNew))input.CopyTo(stream);
            files.Add(new(relative,new FileInfo(target).Length,SafeFiles.Hash(target)));
        }
    }
}
