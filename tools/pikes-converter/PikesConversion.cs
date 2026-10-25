using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.CustomTracks;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Data;

namespace DiRT2VR.Pikes;

public static class PikesConversion
{
    public static ConversionProfile GetProfile()
    {
        var sources=JsonSerializer.Deserialize<Fingerprint[]>(PikesResources.Text("sources.json"))!;
        TrackPack.ValidateSources(sources,"dirtrally");var pack=TrackPacks.Pikes;
        return new(pack.Id,pack.Name,pack.Version,pack.MinimumLauncher,64L<<30,12L<<30,pack.Modes.ToArray(),pack.Layouts.ToArray(),sources);
    }
    public static int Run(string[] args)
    {
        try
        {
            const string usage="Usage: DiRT2VR.exe --convert-pikes <DiRT Rally folder> <DiRT 2 folder> <new output folder> [--layouts ids] [--performance-assets]";
            if(args.Length<4||args[0]!="--convert-pikes"||args.Skip(1).Take(3).Any(string.IsNullOrWhiteSpace))throw new IOException(usage);
            string[]? layouts=null;bool performanceAssets=false;
            for(int i=4;i<args.Length;i++)
            {
                if(args[i]=="--performance-assets"&&!performanceAssets)performanceAssets=true;
                else if(args[i]=="--layouts"&&layouts is null&&i+1<args.Length)layouts=args[++i].Split(',');
                else throw new IOException(usage);
            }
            if(OperatingSystem.IsWindows()&&new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new IOException("Start the track converter normally, without Run as administrator.");
            Build(args[1],args[2],args[3],layouts,p=>{Console.WriteLine(JsonSerializer.Serialize(p));Console.Out.Flush();},performanceAssets);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
    public static void Build(string rally,string dirt2,string output,IEnumerable<string>? layoutIds=null,Action<TrackProgress>? progress=null,bool performanceAssets=false)
    {
        SafeFiles.RequireClosed();rally=Path.GetFullPath(rally);dirt2=Path.GetFullPath(dirt2);output=Path.GetFullPath(output);
        var pack=TrackPacks.Pikes;var selected=pack.SelectLayouts(layoutIds);var profile=GetProfile();
        Files.NewOutput(output,rally,dirt2);
        void Report(int percent,string message)=>progress?.Invoke(new(percent,message));
        Report(0,"Checking your DiRT Rally and DiRT 2 source files");
        var sources=TrackPack.VerifySources(profile,dirt2,rally,null,default);
        var expected=sources.ToDictionary(f=>Path.GetFullPath(Path.Combine(f.Game=="dirt2"?dirt2:rally,f.Path)),f=>f.Sha256,StringComparer.OrdinalIgnoreCase);
        string schema=Path.Combine(output,"schemaDirt2.xml");
        using(var input=typeof(Aspen.AspenConversion).Assembly.GetManifestResourceStream("schemaDirt2.xml")!)
        using(var target=File.Create(schema))input.CopyTo(target);
        var database=new DatabaseFile(Path.Combine(dirt2,"database/database.bin"),schema);
        int donor=(int)database.Tables["track_model"]!.Rows.Cast<System.Data.DataRow>().Single(r=>(string)r["file_string"]=="battersea"&&(string)r["route_string"]=="route_1")["id"];
        string effects=Path.Combine(output,"pikes-effects.xml");
        var global=PikesSourceProfile.ReadXml(Path.Combine(dirt2,"postprocess/effects.xml"));
        if(global.Descendants("Effect").Any(e=>(string?)e.Attribute("name")=="PikesFocusProbe"))throw new IOException("Restore original post-processing files before building Pikes Peak.");
        var effectParent=global.Descendants("Effect").First().Parent!;effectParent.Add(XElement.Parse(PikesResources.Text("focus.xml")));Files.Xml(global,effects);
        var files=new List<PackFile>();var sessions=new List<LayoutSession>();string install=Path.Combine(output,"install");
        for(int index=0;index<selected.Length;index++)
        {
            var layout=selected[index];SafeFiles.RequireClosed();
            Report(5+index*85/selected.Length,"Building "+layout.Name+" from your original assets");
            string candidate=Path.Combine(output,"build-"+layout.Id);
            bool tarmac=layout.Id=="pikes-peak-tarmac";string condition=layout.Id switch{"pikes-peak-gravel-alt"=>"day_alt","pikes-peak-gravel-wet"=>"wet",_=>"day"};
            PikesGravelBuild.Run(rally,dirt2,tarmac?"tarmac":"gravel",condition,candidate,
                (percent,message)=>Report(5+(index*85+percent*85/100)/selected.Length,layout.Name+": "+message),performanceAssets);
            foreach(var input in SafeFiles.ReadJson<Dictionary<string,string>>(Path.Combine(candidate,"inputs.json")))
                if(!expected.TryGetValue(Path.GetFullPath(input.Key),out var hash)||hash!=input.Value)throw new IOException("Conversion used an unsupported source file: "+input.Key);
            using var validation=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(candidate,"validation.json")));
            if(!validation.RootElement.GetProperty("Passed").GetBoolean())throw new IOException("Pikes Peak conversion validation failed.");
            float length=validation.RootElement.GetProperty("LengthMetres").GetSingle();
            foreach(var file in Directory.EnumerateFiles(candidate).Where(p=>PikesGravelConditions.GameFile(Path.GetFileName(p))))
            {
                string name=Path.GetFileName(file);if(name is "shaderpack.pssg" or "postprocess.pssg")continue;
                Copy(file,$"tracks/usa/{layout.Folder}/route_0/{name}");
            }
            foreach(var file in Directory.EnumerateFiles(Path.Combine(candidate,"shared")))Copy(file,$"tracks/usa/{layout.Folder}/{Path.GetFileName(file)}");
            foreach(string start in new[]{"race","grid-race"})foreach(string name in SessionFiles.PikesRaceFiles)
                Copy(Path.Combine(candidate,start,name),$"{pack.Support}/{layout.Id}/{start}/{name}");
            string metadata=Path.Combine(output,"metadata-"+layout.Id);
            TrackMetadata.Create(dirt2,schema,metadata,new(layout.Folder,layout.Folder[5..],"PIKES PEAK", "pikes","PIKES PEAK",donor),length,false);
            var session=new List<SessionFile>();
            using(var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(metadata,"metadata.json"))))
                foreach(var entry in manifest.RootElement.GetProperty("Files").EnumerateArray())
                {
                    string path=entry.GetProperty("Path").GetString()!;Session(path,SafeFiles.Inside(metadata,path));
                }
            Session("dx11/shaderpack/shaderpack.pssg",Path.Combine(candidate,"shaderpack.pssg"));
            Session("dx11/shaderpack/postprocess.pssg",Path.Combine(candidate,"postprocess.pssg"));
            Session("postprocess/effects.xml",effects);
            sessions.Add(new(layout.Id,session.ToArray(),pack.Version,performanceAssets));
            void Session(string path,string source)
            {
                string owned=$"{pack.Support}/{layout.Id}/{path}";Copy(source,owned);
                session.Add(new(path,expected[Path.GetFullPath(Path.Combine(dirt2,path))],owned));
            }
        }
        Report(95,"Verifying the complete pack and unchanged original files");
        if(!sources.SequenceEqual(TrackPack.VerifySources(profile,dirt2,rally,null,default)))throw new IOException("Source files changed during conversion. Retry with original files unchanged.");
        var receipt=new PackReceipt(2,pack.Id,pack.Version,pack.MinimumLauncher,files.ToArray(),sessions.ToArray(),sources);
        pack.Verify(install,receipt);SafeFiles.WriteJson(SafeFiles.Inside(install,pack.Receipt),receipt);
        Report(100,"Pikes Peak is ready for Direct practice, Rally and Grid race on desktop or in VR");
        void Copy(string source,string relative)
        {
            string target=SafeFiles.Inside(install,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);SafeFiles.NoLinks(source);
            using(var input=File.OpenRead(source))using(var stream=new FileStream(target,FileMode.CreateNew))input.CopyTo(stream);
            files.Add(new(relative,new FileInfo(target).Length,SafeFiles.Hash(target)));
        }
    }
}
