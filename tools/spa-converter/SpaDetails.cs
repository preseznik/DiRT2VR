using System.Numerics;
using System.Text.Json;
using DiRT2VR.Nordschleife;

internal static class SpaDetails
{
    internal static void Run(string source,string game,string baseline,string output)
    {
        source=Path.GetFullPath(source);game=Path.GetFullPath(game);baseline=Path.GetFullPath(baseline);output=Path.GetFullPath(output);
        Files.NewOutput(output,source,game,baseline);
        using var validation=JsonDocument.Parse(File.ReadAllBytes(Files.Inside(baseline,"validation.json")));
        var pins=validation.RootElement.GetProperty("Files").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!);
        foreach(var (file,hash) in pins)if(Files.Hash(Files.Inside(baseline,file))!=hash)throw new InvalidDataException("Baseline changed: "+file);
        using var segment=JsonDocument.Parse(File.ReadAllBytes(Files.Inside(baseline,"segment.json")));
        Vector3 V(JsonElement e){var a=e.EnumerateArray().Select(v=>v.GetSingle()).ToArray();return new(a[0],a[1],a[2]);}
        var origin=V(segment.RootElement.GetProperty("TranslationOnly"));
        using var gateFile=JsonDocument.Parse(File.ReadAllBytes(Files.Inside(baseline,"gates.json")));
        var gates=gateFile.RootElement.EnumerateArray().Select(g=>new Gate(V(g.GetProperty("Position")),V(g.GetProperty("Tangent")),g.GetProperty("Left").GetSingle(),g.GetProperty("Right").GetSingle(),g.GetProperty("Distance").GetSingle())).ToArray();
        var grass=SpaGrass.ReadBaked(source,output,origin,gates,radius:35,density:1,maximumCards:120000,batchTileSize:64);
        SpaDetailAppend.Append(baseline,output,grass);
        Directory.CreateDirectory(Path.Combine(output,"shared"));File.Move(Path.Combine(output,"tracksplit.pssg"),Path.Combine(output,"shared/tracksplit.pssg"));
        SpaCrowds.Write(source,game,output,origin);
        SpaDetailVisibility.Write(game,baseline,output);
        SpaMarkerVisibility.Write(game,baseline,output);
        foreach(var (file,hash) in pins){string target=Files.Inside(output,file);if(!File.Exists(target)){Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Files.Inside(baseline,file),target);}}
        foreach(string file in new[]{"segment.json","gates.json","source-boundaries.json"})File.Copy(Files.Inside(baseline,file),Path.Combine(output,file));
        foreach(string file in new[]{"track.jpk","boundarylines.cqtc"})if(Files.Hash(Files.Inside(output,file))!=pins[file])throw new InvalidDataException("Accepted collision changed");
        var files=pins.Keys.Concat(new[]{"ornament_attributes.xml","crowd_standing.bin","crowd_standing2.bin","organism_track_dataset.xml"}).ToDictionary(p=>p,p=>Files.Hash(Files.Inside(output,p)));
        Files.Json(Path.Combine(output,"geometry-check.json"),new{Passed=true,CollisionFilesByteIdentical=true,SourceBaseline=baseline,AddedCollision=false,RuntimeValidated=false});
        Files.Json(Path.Combine(output,"validation.json"),new{Passed=true,RuntimeValidated=false,Files=files});
        Console.WriteLine("Spa details candidate prepared: "+output);
    }
}
