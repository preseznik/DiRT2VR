using System.Buffers.Binary;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Xml;

internal static class DetailProbe
{
    internal static XDocument Xml(string path){using var stream=File.OpenRead(path);return XDocument.Parse(new XmlFile(stream).Document.OuterXml);}
    internal static void Prepare(string rally,string dirt,string candidate,string output,PikesSourceProfile? profile=null)
    {
        Files.NewOutput(output,rally,dirt,candidate);
        profile??=PikesSourceProfile.Modern;string venue=profile.PathIn(rally);
        using var segment=JsonDocument.Parse(File.ReadAllBytes(Files.Inside(candidate,"segment.json")));float[] origin=segment.RootElement.GetProperty("TranslationOnly").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
        var source=profile.Lighting(rally);var lighting=Xml(Files.Inside(candidate,"shared/lighting.xml"));var original=new XDocument(lighting);
        string[] names=["fogDistance","fogDensityCap","fogColour","fogColourBrightness","fogHazeColour","fogHazeBrightness","fogHazeColour2","fogHazeBrightness2","fogHazeSunCurvePower"];
        var changes=new List<object>();
        foreach(string name in names){var to=lighting.Descendants("param").Single(p=>p.Attribute(name) is not null).Attribute(name)!;var from=source.Descendants("param").Single(p=>p.Attribute(name) is not null).Attribute(name)!;changes.Add(new{Name=name,Before=to.Value,After=from.Value});to.Value=from.Value;}
        Files.Xml(lighting,Path.Combine(output,"lighting.xml"));
        var reverted=new XDocument(Xml(Path.Combine(output,"lighting.xml")));foreach(string name in names)reverted.Descendants("param").Single(p=>p.Attribute(name) is not null).SetAttributeValue(name,original.Descendants("param").Single(p=>p.Attribute(name) is not null).Attribute(name)!.Value);if(!XNode.DeepEquals(original,reverted))throw new InvalidDataException("Unexpected source-fog edit");
        string crowdPath=Files.Inside(venue,"route_0/crowd_standing2.bin"),definitions=Files.Inside(dirt,"anims/crowdDefs.xml");byte[] crowdSource=File.ReadAllBytes(crowdPath);
        var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(string suffix in new[]{"A","B","C","D","E","F","G","H"})aliases["m_static_var_"+suffix]="m_cr";
        foreach(string prefix in new[]{"pre-race_marshal_","race_marshal_"})foreach(string suffix in new[]{"01","02","03"})aliases[prefix+suffix]="mar_sta_fl";
        foreach(string prefix in new[]{"race_photographer_","pre-race_photographer_"})foreach(string suffix in new[]{"01","02"})aliases[prefix+suffix]="m_photo_"+suffix;
        aliases["m_anim_static_01"]="m_cr";aliases["f_anim_static_01"]="f_cr";
        var report=new List<object>();byte[] normalized=NormalizeRallyCrowds(crowdSource);byte[] crowd=Crowds.Convert(normalized,Xml(definitions),report,includeDayOnly:true,venueAliases:aliases);
        int I(int at)=>BinaryPrimitives.ReadInt32LittleEndian(crowd.AsSpan(at));
        void Shift(int at,float delta){float value=BinaryPrimitives.ReadSingleLittleEndian(crowd.AsSpan(at));if(!float.IsFinite(value))throw new InvalidDataException("Invalid crowd bounds/position");BinaryPrimitives.WriteSingleLittleEndian(crowd.AsSpan(at),value-delta);}
        Shift(12,origin[0]);Shift(16,origin[2]);int blocks=I(36);for(int n=0;n<blocks;n++)for(int bound=0;bound<2;bound++)for(int axis=0;axis<3;axis++)Shift(48+n*40+16+bound*12+axis*4,origin[axis]);
        int header=I(8),table=I(header+16),count=0;for(int n=0;n<I(header);n++)for(int j=0;j<I(table+n*16+4);j++){int at=I(table+n*16+8)+j*40;for(int axis=0;axis<3;axis++)Shift(at+8+axis*4,origin[axis]);count++;}
        if(count!=I(header+4))throw new InvalidDataException("Crowd count changed");
        File.WriteAllBytes(Path.Combine(output,"crowd_standing2.bin"),crowd);
        File.Copy(Files.Inside(dirt,"tracks/london/battersea/route_1/organism_track_dataset.xml"),Path.Combine(output,"organism_track_dataset.xml"));
        Files.Json(Path.Combine(output,"detail-probe.json"),new{SourceCandidate=Path.GetFullPath(candidate),SourceFogChanges=changes,CrowdSourceHash=Files.Hash(crowdPath),CrowdDefinitionHash=Files.Hash(definitions),CrowdInstances=count,CrowdTypes=report,NativeDirt2PeopleAtOriginalSourcePlacements=true,OriginalSourceCrowdMeshesNotPorted=true,DepthOfFieldPipelinePresentButGameplayActivationUnverified=true,GrassAndNettingPending=true,RuntimeValidated=false});
        var files=new[]{"lighting.xml","crowd_standing2.bin","organism_track_dataset.xml"}.ToDictionary(n=>n,n=>Files.Hash(Path.Combine(output,n)));
        Files.Json(Path.Combine(output,"car-lighting-probe.json"),new{SourceCandidate=Path.GetFullPath(candidate),RenderingProbeOnly=true,Purpose="Original daylight distance fog and translated native crowd reconstruction",Files=files});
        Console.WriteLine($"Source fog and {count} native crowd placements prepared.");
    }
    static byte[] NormalizeRallyCrowds(byte[] source)
    {
        int I(int p)=>BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(p,4));float F(int p)=>BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(p,4));
        if(I(0)!=12||I(4)!=36||I(40)!=48||I(36)!=I(44)||I(8)!=48+I(36)*40)throw new InvalidDataException("Unsupported Rally crowd header");
        int header=I(8),table=I(header+16),types=I(header),total=I(header+4);if(table!=header+24||types!=I(header+20)||table!=I(header+8))throw new InvalidDataException("Invalid crowd references");
        var blocks=Enumerable.Range(0,I(36)).ToDictionary(n=>I(48+n*40+4),n=>n);var counts=new int[blocks.Count];
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);writer.Write(source,0,table+types*16);
        void Put(int p,int value){long saved=output.Position;output.Position=p;writer.Write(value);output.Position=saved;}
        var names=new List<string>();int placed=0,expected=table+types*16;
        for(int n=0;n<types;n++){
            int row=table+n*16,count=I(row+4),start=I(row+8);if(start!=expected||count<1||count!=I(row+12))throw new InvalidDataException("Invalid 36-byte crowd span");expected=checked(start+count*36);
            int name=I(row),end=Array.IndexOf(source,(byte)0,name);if(name<expected||end<name)throw new InvalidDataException("Invalid crowd name");names.Add(System.Text.Encoding.ASCII.GetString(source,name,end-name));Put(row+8,checked((int)output.Position));
            for(int j=0;j<count;j++){
                int at=start+j*36;float x=(F(at+8)-F(12))/F(20),z=(F(at+16)-F(16))/F(24);
                if(x<-.01f/F(20)||z<-.01f/F(24)||x>=I(28)||z>=I(32))throw new InvalidDataException("Crowd position outside original grid");
                int cell=Math.Max(0,(int)MathF.Floor(x))+Math.Max(0,(int)MathF.Floor(z))*I(28);if(!blocks.TryGetValue(cell,out int block))throw new InvalidDataException("Crowd has no source visibility block");
                counts[block]++;placed++;writer.Write(source,at,36);writer.Write(block);writer.Write(0);
            }
        }
        if(placed!=total)throw new InvalidDataException("Crowd placement total mismatch");for(int n=0;n<counts.Length;n++)if(counts[n]!=I(48+n*40+8))throw new InvalidDataException("Recovered crowd block counts differ from source");
        for(int n=0;n<types;n++){Put(table+n*16,checked((int)output.Position));writer.Write(System.Text.Encoding.ASCII.GetBytes(names[n]));writer.Write((byte)0);}
        return output.ToArray();
    }
}

// The shared crowd decoder's unused install entrypoint needs these file helpers.
// Keep this private adapter independent of the Aspen entity conversion pipeline.
internal static class PortFiles
{
    internal static void NoLinks(string path)=>Files.NoLinks(path);
    internal static string Hash(string path)=>Files.Hash(path);
    internal static XDocument ReadXml(string path)=>DetailProbe.Xml(path);
}
