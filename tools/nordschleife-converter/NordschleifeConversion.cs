using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Data;
using EgoEngineLibrary.Xml;

namespace DiRT2VR.Nordschleife;

public static class NordschleifeConversion
{
    public static ConversionProfile GetProfile(bool highQualityAssets=false,string? acRoot=null)
    {
        using var stream=typeof(NordschleifeConversion).Assembly.GetManifestResourceStream("nordschleife-sources.json")!;
        var sources=JsonSerializer.Deserialize<Fingerprint[]>(stream)!;
        if(highQualityAssets)
        {
            using var extra=typeof(NordschleifeConversion).Assembly.GetManifestResourceStream("nordschleife-visual-sources.json")!;
            sources=[..sources,..JsonSerializer.Deserialize<Fingerprint[]>(extra)!];
            const string vao="extension/vao-patches/ks_nordschleife.vao-patch";
            if(acRoot is not null&&File.Exists(SafeFiles.Inside(acRoot,vao)))
                sources=[..sources,new("assettocorsa",vao,"F449D09BB03444023630FF05FF8FE510AF6B13E06B028681792CD67462AE1571")];
        }
        TrackPack.ValidateSources(sources,"assettocorsa");
        var pack=TrackPacks.Nordschleife;
        return new(pack.Id,pack.Name,pack.Version,highQualityAssets?TrackPacks.NordHighQualityLauncher:pack.MinimumLauncher,12L<<30,3L<<30,pack.Modes.ToArray(),pack.Layouts.ToArray(),sources);
    }
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length is not (4 or 5)||args[0]!="--convert-nordschleife"||args.Skip(1).Take(3).Any(string.IsNullOrWhiteSpace)||(args.Length==5&&args[4]!="--high-quality-assets"))
                throw new IOException("Usage: DiRT2VR.exe --convert-nordschleife <Assetto Corsa folder> <DiRT 2 folder> <new output folder> [--high-quality-assets]");
            if(OperatingSystem.IsWindows()&&new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new IOException("Start the track converter normally, without Run as administrator.");
            Build(args[1],args[2],args[3],p=>{Console.WriteLine(JsonSerializer.Serialize(p));Console.Out.Flush();},args.Length==5);
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
    public static void Build(string ac,string d2,string output,Action<TrackProgress>? progress,bool highQualityAssets=false)
    {
        SafeFiles.RequireClosed();ac=Path.GetFullPath(ac);d2=Path.GetFullPath(d2);output=Path.GetFullPath(output);
        Files.NewOutput(output,ac,d2);
        var profile=GetProfile(highQualityAssets,ac);var pack=TrackPacks.Nordschleife;
        void Report(int percent,string message)=>progress?.Invoke(new(percent,message));
        Report(0,"Checking your Assetto Corsa and DiRT 2 source files");
        var sources=TrackPack.VerifySources(profile,d2,ac,null,default);
        var expected=sources.ToDictionary(f=>Path.GetFullPath(Path.Combine(f.Game=="dirt2"?d2:ac,f.Path)),f=>f.Sha256,StringComparer.OrdinalIgnoreCase);
        string schema=Path.Combine(output,"schemaDirt2.xml");
        using(var input=typeof(Aspen.AspenConversion).Assembly.GetManifestResourceStream("schemaDirt2.xml")!)
        using(var target=File.Create(schema))input.CopyTo(target);
        var database=new DatabaseFile(Path.Combine(d2,"database/database.bin"),schema);
        int donorModel=(int)database.Tables["track_model"]!.Rows.Cast<System.Data.DataRow>().Single(r=>(string)r["file_string"]=="battersea"&&(string)r["route_string"]=="route_1")["id"];
        string candidate=Path.Combine(output,"build");
        Report(10,"Converting the full circuit, original collision and all scenery");
        TrackBuild.Run(ac,d2,candidate,0,fullCourse:true,highQualityAssets:highQualityAssets);
        foreach(var (path,hash) in SafeFiles.ReadJson<Dictionary<string,string>>(Path.Combine(candidate,"inputs.json")))
            if(!expected.TryGetValue(Path.GetFullPath(path),out var pin)||pin!=hash)throw new IOException("Conversion used an unsupported source file: "+path);
        using var validation=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(candidate,"validation.json")));
        float length=validation.RootElement.GetProperty("LengthMetres").GetSingle();
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
                if(f.Name.StartsWith("support/",StringComparison.Ordinal))continue;
                string source=SafeFiles.Inside(candidate,f.Name);
                if(SafeFiles.Hash(source)!=f.Value.GetString())throw new IOException("Candidate changed while preparing the pack: "+f.Name);
                assets[f.Name.StartsWith("shared/",StringComparison.Ordinal)?f.Name[7..]:"route_0/"+f.Name]=source;
            }
            XDocument Xml(string name){using var input=File.OpenRead(SafeFiles.Inside(d2,donor+name));return XDocument.Parse(new XmlFile(input).Document.OuterXml);}
            var lighting=Xml("lighting.xml");var effects=Xml("effects.xml");Lighting.Apply(lighting,effects,layout.Condition);
            foreach(var (name,doc) in new[]{("lighting.xml",lighting),("effects.xml",effects)})
            {
                string path=Path.Combine(variant,name);Files.Xml(doc,path);assets[name]=path;
            }
            foreach(var (relative,source) in assets)Copy(source,"tracks/usa/"+layout.Folder+"/"+relative);
            string metadata=Path.Combine(variant,"metadata");
            string key=layout.Id switch {"nordschleife-daylight"=>"nord_day","nordschleife-overcast"=>"nord_cloud",_=>"nord_even"};
            TrackMetadata.Create(d2,schema,metadata,new(layout.Folder,key,"NORDSCHLEIFE", "nord","NORDSCHLEIFE",donorModel),length,false);
            var session=new List<SessionFile>();
            using(var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(metadata,"metadata.json"))))
                foreach(var entry in manifest.RootElement.GetProperty("Files").EnumerateArray())
                {
                    string path=entry.GetProperty("Path").GetString()!;string owned=pack.Support+"/"+layout.Id+"/"+path;
                    Copy(SafeFiles.Inside(metadata,path),owned);
                    session.Add(new(path,expected[Path.GetFullPath(Path.Combine(d2,path))],owned));
                }
            if(highQualityAssets)
            {
                const string path="dx11/shaderpack/shaderpack.pssg";
                string source=SafeFiles.Inside(candidate,"support/shaderpack.pssg");
                if(SafeFiles.Hash(source)!=validation.RootElement.GetProperty("Files").GetProperty("support/shaderpack.pssg").GetString())throw new IOException("Visual shaders changed after validation.");
                string owned=pack.Support+"/"+layout.Id+"/"+path;Copy(source,owned);
                session.Add(new(path,expected[Path.GetFullPath(Path.Combine(d2,path))],owned));
            }
            sessions.Add(new(layout.Id,session.ToArray(),highQualityAssets?pack.Version:null));
        }
        Report(95,"Verifying every installed file and original source");
        if(!sources.SequenceEqual(TrackPack.VerifySources(profile,d2,ac,null,default)))
            throw new IOException("Source files changed during conversion. Retry with the original files unchanged.");
        var receipt=new PackReceipt(highQualityAssets?2:1,pack.Id,pack.Version,profile.MinimumLauncher,files.ToArray(),sessions.ToArray(),sources,highQualityAssets);
        pack.Verify(install,receipt);SafeFiles.WriteJson(SafeFiles.Inside(install,pack.Receipt),receipt);
        Report(100,"Nordschleife is ready for desktop and experimental VR practice and Race");
        void Copy(string source,string relative)
        {
            string target=SafeFiles.Inside(install,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            SafeFiles.NoLinks(source);using(var input=File.OpenRead(source))using(var stream=new FileStream(target,FileMode.CreateNew))input.CopyTo(stream);
            files.Add(new(relative,new FileInfo(target).Length,SafeFiles.Hash(target)));
        }
    }
}
