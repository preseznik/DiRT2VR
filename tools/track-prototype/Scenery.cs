using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using EgoEngineLibrary.Graphics.Pssg;

internal static class Scenery
{
    // Keep slots and IDs stable for visibility, physics and attached-object references.
    // Suppressed placements are parked below the world rather than renumbered/deleted.
    const float HiddenY = -10000;
    static bool Background(string name) => name.Contains("distant",StringComparison.OrdinalIgnoreCase) || name.StartsWith("horizon_",StringComparison.OrdinalIgnoreCase);
    internal static void Write(string source,string output)
    {
        var hiddenObjects=new List<string>(); var keptObjects=new List<string>();
        using(var input=File.OpenRead(Path.Combine(source,"objects.ens")))
        {
            var file=PssgFile.Open(input);
            foreach(var instance in file.Elements<PssgElement>().Where(e=>e.Name is "TEMPLATEBASICENTITYINSTANCE" or "TEMPLATEENTITYINSTANCE"))
            {
                var id=instance.GetAttributeValue<string>("id");
                if(Background(id)) { keptObjects.Add(id); continue; }
                var transform=instance.ChildElements.Single(e=>e.Name=="TEMPLATETRANSFORM");
                if(transform.Value.Length!=64) throw new InvalidDataException("Unexpected entity transform layout.");
                BinaryPrimitives.WriteSingleBigEndian(transform.Value.AsSpan(52,4),HiddenY);
                hiddenObjects.Add(id);
            }
            using var destination=new FileStream(Path.Combine(output,"objects.ens"),FileMode.CreateNew);
            file.Save(destination);
        }
        // Ornaments have two synchronized representations: XML and packed LE matrices.
        var doc=XDocument.Load(Path.Combine(source,"ornaments.xml"));
        var bytes=File.ReadAllBytes(Path.Combine(source,"ornaments.bin"));
        var original=bytes.ToArray();
        int I(int p)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p,4));
        var groups=doc.Root!.Elements("instanceref").ToArray();
        if(I(0)!=0 || I(36)!=44 || I(40)!=groups.Length || I(28)!=(int)doc.Root.Attribute("total_instances")!)
            throw new InvalidDataException("Unsupported ornament header.");
        var hiddenGroups=new List<string>(); var keptGroups=new List<string>();
        var permittedOffsets=new HashSet<int>(); int hiddenOrnaments=0,keptOrnaments=0;
        for(int g=0;g<groups.Length;g++)
        {
            var group=groups[g]; var name=(string)group.Attribute("filename")!;
            int record=44+g*56, stringOffset=I(record), matrixOffset=I(record+44), count=I(record+48);
            var instances=group.Elements("instance").ToArray();
            int stringEnd=Array.IndexOf(bytes,(byte)0,stringOffset);
            if(stringEnd<0 || Encoding.UTF8.GetString(bytes,stringOffset,stringEnd-stringOffset)!=name ||
               count!=instances.Length || matrixOffset<44+groups.Length*56 || matrixOffset+count*64>bytes.Length ||
               I(record+36)!=(int)group.Attribute("max_instances")! || I(record+40)!=(int)group.Attribute("offset")!)
                throw new InvalidDataException("Ornament XML/binary mismatch: "+name);
            bool keep=Background(name);
            (keep?keptGroups:hiddenGroups).Add(name);
            for(int i=0;i<count;i++)
            {
                var matrix=((string)instances[i].Attribute("transform")!).Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                if(matrix.Length!=16) throw new InvalidDataException("Unexpected ornament transform.");
                int[] packedIndices={0,1,2,4,5,6,8,9,10,12,13,14};
                for(int j=0;j<12;j++)
                    if(Math.Abs(matrix[packedIndices[j]]-BitConverter.ToSingle(bytes,matrixOffset+i*64+j*4))>.00005f)
                        throw new InvalidDataException("Ornament transform mismatch: "+name);
                if(keep) { keptOrnaments++; continue; }
                matrix[13]=HiddenY;
                instances[i].SetAttributeValue("transform",string.Join(" ",matrix.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)))+" ");
                int yOffset=matrixOffset+i*64+40;
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(yOffset,4),HiddenY);
                for(int k=0;k<4;k++) permittedOffsets.Add(yOffset+k);
                hiddenOrnaments++;
            }
        }
        if(Enumerable.Range(0,bytes.Length).Any(i=>bytes[i]!=original[i]&&!permittedOffsets.Contains(i)))
            throw new InvalidDataException("Unexpected ornament binary edit.");
        doc.Save(Path.Combine(output,"ornaments.xml"));
        File.WriteAllBytes(Path.Combine(output,"ornaments.bin"),bytes);
        // Read the saved ENS independently and check that no local placement stayed above ground.
        using(var input=File.OpenRead(Path.Combine(output,"objects.ens")))
        {
            var saved=PssgFile.Open(input);
            var all=saved.Elements<PssgElement>().Where(e=>e.Name is "TEMPLATEBASICENTITYINSTANCE" or "TEMPLATEENTITYINSTANCE").ToArray();
            if(all.Length!=hiddenObjects.Count+keptObjects.Count) throw new InvalidDataException("Object slots changed.");
            foreach(var instance in all.Where(e=>!Background(e.GetAttributeValue<string>("id"))))
                if(BinaryPrimitives.ReadSingleBigEndian(instance.ChildElements.Single(e=>e.Name=="TEMPLATETRANSFORM").Value.AsSpan(52,4))!=HiddenY)
                    throw new InvalidDataException("Local entity suppression failed.");
        }
        File.WriteAllText(Path.Combine(output,"scenery.json"),JsonSerializer.Serialize(new {
            HiddenObjects=hiddenObjects.Count, KeptObjects=keptObjects, HiddenOrnaments=hiddenOrnaments, KeptOrnaments=keptOrnaments,
            HiddenGroups=hiddenGroups, KeptGroups=keptGroups, HiddenY, SlotsAndIdsPreserved=true,
            TreesAndSkyUnchanged=true, RuntimeValidated=false
        },new JsonSerializerOptions {WriteIndented=true}));
    }
}
