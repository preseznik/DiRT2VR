using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

internal static class PikesCrowdFix
{
    sealed record Row(string Name,int Start,int Count);
    static int I(byte[] bytes,int at)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at,4));
    static float F(byte[] bytes,int at)=>BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at,4));
    static string Name(byte[] bytes,int at)
    {
        if(at<0||at>=bytes.Length)throw new InvalidDataException("Crowd name outside file");int end=Array.IndexOf(bytes,(byte)0,at);
        if(end<at||end-at>128)throw new InvalidDataException("Invalid crowd name");return Encoding.ASCII.GetString(bytes,at,end-at);
    }
    static XDocument Xml(string path){using var stream=File.OpenRead(path);return XDocument.Parse(new XmlFile(stream).Document.OuterXml);}
    static Row[] Rows(byte[] bytes,int stride,bool source)
    {
        if(I(bytes,0)!=12||I(bytes,4)!=36)throw new InvalidDataException("Unexpected crowd file header");
        int header=I(bytes,8),blocks=I(bytes,36),table=I(bytes,header+16),count=I(bytes,header+20);
        if(blocks<1||I(bytes,40)!=48||I(bytes,44)!=blocks||header!=48+blocks*40||count<1||count>4096)throw new InvalidDataException("Unexpected crowd visibility/reference header");
        if(source&&(I(bytes,header+12)!=0||I(bytes,header)!=count||table!=header+24||I(bytes,header+8)!=table))throw new InvalidDataException("Only stationary candidate crowds are supported");
        var rows=Enumerable.Range(0,count).Select(n=>
        {
            int row=table+n*16,start=I(bytes,row+8),instances=I(bytes,row+4),nameOffset=I(bytes,row);
            if(instances<1||instances!=I(bytes,row+12)||start<0||(long)start+(long)instances*stride>bytes.Length||nameOffset<start+(long)instances*stride)throw new InvalidDataException("Invalid crowd placement span");
            return new Row(Name(bytes,nameOffset),start,instances);
        }).ToArray();
        if(source)
        {
            int expected=table+count*16;foreach(var row in rows){if(row.Start!=expected)throw new InvalidDataException("Noncontiguous source crowd records");expected=checked(expected+row.Count*stride);}
            if(rows.Sum(r=>r.Count)!=I(bytes,header+4))throw new InvalidDataException("Source crowd total mismatch");
        }
        return rows;
    }
    static string Role(string name)=>name switch
    {
        "f_cr" or "f_anim_static_01"=>"f_cr",
        "m_cr" or "m_anim_static_01" or "m_cameraman_01" or "m_flare_01" or "m_photo_01" or "m_photo_02"=>"m_cr",
        _ when name.StartsWith("m_static_var_",StringComparison.Ordinal)&&name.Length==14&&name[^1] is >= 'A' and <= 'H'=>"m_cr",
        "mar_sta_fl" or "ma_np"=>"ma_np",
        _ when name.StartsWith("pre-race_marshal_",StringComparison.Ordinal)||name.StartsWith("race_marshal_",StringComparison.Ordinal)=>"ma_np",
        _ when name.StartsWith("pre-race_photographer_",StringComparison.Ordinal)||name.StartsWith("race_photographer_",StringComparison.Ordinal)=>"m_cr",
        _=>throw new InvalidDataException("Unknown Pikes crowd source role: "+name)
    };
    internal static void Prepare(string candidate,string dirt,string output,int? expectedCount=3274)
    {
        Files.NewOutput(output,dirt,candidate);
        string sourcePath=Files.Inside(candidate,"crowd_standing2.bin"),donorPath=Files.Inside(dirt,"tracks/london/battersea/route_1/crowd_standing2.bin"),definitionsPath=Files.Inside(dirt,"anims/crowdDefs.xml"),datasetPath=Files.Inside(dirt,"tracks/london/battersea/route_1/organism_track_dataset.xml");
        byte[] source=File.ReadAllBytes(sourcePath),donor=File.ReadAllBytes(donorPath);var sourceRows=Rows(source,40,true);var donorRows=Rows(donor,40,false).ToDictionary(r=>r.Name,StringComparer.Ordinal);
        var definitions=Xml(definitionsPath);var dataset=Xml(datasetPath);
        var inputs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{sourcePath,Files.Hash(sourcePath)},{donorPath,Files.Hash(donorPath)},{definitionsPath,Files.Hash(definitionsPath)},{datasetPath,Files.Hash(datasetPath)}};
        var bindings=dataset.Descendants("pssg").ToDictionary(e=>(string)e.Attribute("userdata")!,e=>(string)e.Attribute("filename")!,StringComparer.Ordinal);
        var required=new Dictionary<string,string>{{"m_cr","maleVariant0"},{"f_cr","femaleVariant0"},{"ma_np","marshall"}};
        var dependencyReport=new List<object>();
        foreach(var (role,meshTag) in required)
        {
            var set=definitions.Descendants("instanceSet").Single(e=>(string?)e.Attribute("name")==role);string map=(string)set.Element("map")!.Attribute("name")!;
            var maps=definitions.Descendants("instanceMap").Where(e=>(string?)e.Attribute("name")==map).ToArray();
            if(maps.Length==0||maps.Any(m=>(string?)m.Attribute("hasSkin")!="1"))throw new InvalidDataException("Fallback actor has no native skin");
            foreach(var actorMap in maps)
            {
                string crowdDef=(string)actorMap.Attribute("crowdDef")!,selector=(string)actorMap.Attribute("animSelector")!;
                var definition=definitions.Descendants("crowdDef").Single(e=>(string?)e.Attribute("name")==crowdDef);
                if((string?)definition.Element("skinData")!.Attribute("meshTag")!=meshTag)throw new InvalidDataException("Fallback skin tag mismatch");
                var animation=definitions.Descendants("animSelector").Single(e=>(string?)e.Attribute("name")==selector);
                if((string?)animation.Attribute("animSet")!="1"||(string?)animation.Attribute("prop")!="None")throw new InvalidDataException("Fallback requires unloaded frontend animation or props");
            }
            if(!bindings.TryGetValue(meshTag,out string? mesh)||!mesh.StartsWith("/data/anims/",StringComparison.Ordinal))throw new InvalidDataException("Fallback skin is not loaded by the dataset");
            string meshPath=Files.Inside(dirt,mesh[6..]);var skin=Files.Pssg(meshPath);int draws=skin.Elements<PssgRenderStreamInstance>().Count();if(draws<1)throw new InvalidDataException("Fallback skin has no drawable geometry");
            inputs.Add(meshPath,Files.Hash(meshPath));dependencyReport.Add(new{Role=role,MeshTag=meshTag,Mesh=meshPath,Draws=draws,HasSkin=true,AnimationSet=1,Prop="None"});
        }
        if(bindings.GetValueOrDefault("hla")!="/data/anims/crowd_anims.pssg")throw new InvalidDataException("Fallback race animation bank is not loaded");
        foreach(string tag in new[]{"hla","billboard","camFlash"}){string path=Files.Inside(dirt,bindings[tag][6..]);inputs.TryAdd(path,Files.Hash(path));}
        int header=I(source,8),table=I(source,header+16),blocks=I(source,36);var blockCounts=new int[blocks];var reports=new List<object>();var rows=new List<(string Target,List<byte[]> Records)>();
        int records=0,parameterChanges=0,sourceBlockMatches=0;
        foreach(var row in sourceRows)
        {
            string target=Role(row.Name);var native=donorRows[target];var converted=new List<byte[]>();
            for(int n=0;n<row.Count;n++)
            {
                int at=row.Start+n*40;for(int j=0;j<6;j++)if(!float.IsFinite(F(source,at+j*4)))throw new InvalidDataException("Invalid source crowd transform/distance");
                int sourceBlock=I(source,at+36);if(sourceBlock<0||sourceBlock>=blocks)throw new InvalidDataException("Source crowd block outside visibility table");
                float x=(F(source,at+8)-F(source,12))/F(source,20),z=(F(source,at+16)-F(source,16))/F(source,24);
                int cell=Math.Max(0,(int)MathF.Floor(x))+Math.Max(0,(int)MathF.Floor(z))*I(source,28);
                if(I(source,48+sourceBlock*40+4)!=cell)throw new InvalidDataException("Source embedded block disagrees with position grid");sourceBlockMatches++;blockCounts[sourceBlock]++;
                byte[] record=new byte[40];source.AsSpan(at,20).CopyTo(record);
                // Candidate15 carries Rally integer metadata in native float slots.
                // Restore the complete native actor parameter span20..35.
                int template=native.Start+(n%native.Count)*40;donor.AsSpan(template+20,16).CopyTo(record.AsSpan(20,16));
                for(int j=0;j<3;j++){float value=F(record,24+j*4);if(!float.IsFinite(value)||value<0||value>1)throw new InvalidDataException("Invalid native donor actor parameter");}
                if(!record.AsSpan(20,16).SequenceEqual(source.AsSpan(at+20,16)))parameterChanges++;
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(36,4),sourceBlock);
                if(!record.AsSpan(0,20).SequenceEqual(source.AsSpan(at,20)))throw new InvalidDataException("Crowd yaw or position changed");
                converted.Add(record);records++;
            }
            rows.Add((target,converted));reports.Add(new{SourceRole=row.Name,TargetRole=target,Instances=row.Count,CandidateStride=40,TargetStride=40,NativeParameterTemplate=target,SourceYawAndPositionRetained=true,SourceAppearanceMetadataUnmapped=true});
        }
        if(records<1||(expectedCount.HasValue&&records!=expectedCount.Value)||records!=I(source,header+4))throw new InvalidDataException("Pikes crowd count mismatch");
        for(int block=0;block<blocks;block++)if(blockCounts[block]!=I(source,48+block*40+8))throw new InvalidDataException("Original crowd visibility counts mismatch");
        var groups=rows.GroupBy(r=>r.Target,StringComparer.Ordinal).ToArray();using var memory=new MemoryStream();using var writer=new BinaryWriter(memory,Encoding.ASCII,true);
        writer.Write(source,0,table);writer.Write(new byte[groups.Length*16]);
        void Put(int at,int value){long saved=memory.Position;memory.Position=at;writer.Write(value);memory.Position=saved;}
        Put(header,groups.Length);Put(header+20,groups.Length);
        for(int n=0;n<groups.Length;n++){int count=groups[n].Sum(r=>r.Records.Count);Put(table+n*16+4,count);Put(table+n*16+12,count);Put(table+n*16+8,checked((int)memory.Position));foreach(var row in groups[n])foreach(var record in row.Records)writer.Write(record);}
        for(int n=0;n<groups.Length;n++){Put(table+n*16,checked((int)memory.Position));writer.Write(Encoding.ASCII.GetBytes(groups[n].Key));writer.Write((byte)0);}
        byte[] result=memory.ToArray();
        if(!result.AsSpan(0,header).SequenceEqual(source.AsSpan(0,header)))throw new InvalidDataException("Crowd grid or bounds changed");
        var validated=Rows(result,40,true);if(validated.Sum(r=>r.Count)!=records)throw new InvalidDataException("Native crowd re-read count mismatch");
        File.WriteAllBytes(Path.Combine(output,"crowd_standing2.bin"),result);File.Copy(datasetPath,Path.Combine(output,"organism_track_dataset.xml"));
        Files.Json(Path.Combine(output,"pikes-crowd-fix.json"),new{SourceCandidate=Path.GetFullPath(candidate),CrowdInstances=records,SourceBlockIdsVerified=sourceBlockMatches,NativeParameterCorrections=parameterChanges,SourceRoleMappings=reports,DrawableFallbackResources=dependencyReport,NativeRecordStride=40,OriginalRallyRecordStride=36,OriginalPositionsAndYawPreserved=true,OriginalVisibilityBlockCountsPreserved=true,UnloadedFrontendStaffRolesReplaced=true,SourceAppearanceMetadataUnmapped=true,NativeActorFields20Through35TakenFromOwnedStockDonor=true,AppearanceApproximation=true,ExactNativeParameterLabelsUnconfirmed=true,PerRecordScaleNotIdentified=true,NativeRandomHeightWidthScaleFromDefinitions=.05,SourceInputs=inputs,NativeRuntimeValidated=false});
        Files.Json(Path.Combine(output,"car-lighting-probe.json"),new{SourceCandidate=Path.GetFullPath(candidate),RenderingProbeOnly=true,Purpose="Correct Rally36 to native40 crowd record parameters and replace unloaded staff roles with drawable race actors",Files=new[]{"crowd_standing2.bin","organism_track_dataset.xml"}.ToDictionary(n=>n,n=>Files.Hash(Path.Combine(output,n)))});
    }
}
