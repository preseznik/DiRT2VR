using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

// Retain the race animation bank and import missing staff clips. Camera meshes
// and props use local DiRT 2 resources. Animation packet IDs are file-local.
internal static class StaffResources
{
    internal static readonly IReadOnlySet<string> Roles=new HashSet<string>(["m_cameraman_01","pre-race_photographer_01"],StringComparer.Ordinal);
    internal static readonly string[] Clips=["m_cameraman_idle","m_cameraman_pan","m_cameraman_adjusting_camera","m_cameraman_come_closer","m_photo_01","m_photo_02","m_photo_idle"];
    static byte[] Bytes(string hex)=>Convert.FromHexString(string.Concat(hex.Where(c=>!char.IsWhiteSpace(c))));
    static string Hex(byte[] bytes)=>string.Join(' ',bytes.Select(b=>b.ToString("X2")))+" ";
    static string Reference(string hex) {
        var bytes=Bytes(hex);int length=BinaryPrimitives.ReadInt32BigEndian(bytes);
        if(length!=bytes.Length-4 || bytes[4]!='#')throw new InvalidDataException("Invalid animation reference.");
        return Encoding.ASCII.GetString(bytes,5,length-1);
    }
    static string ReferenceBytes(string id) {
        var text=Encoding.ASCII.GetBytes("#"+id);var bytes=new byte[text.Length+4];
        BinaryPrimitives.WriteInt32BigEndian(bytes,text.Length);text.CopyTo(bytes,4);return Hex(bytes);
    }
    internal static XDocument Merge(XDocument race,XDocument frontend,bool includePropChannels=true) {
        // Scope this schema to the staff experiment. Registering it globally
        // would also alter the unaccepted ski-lift animation conversion.
        var schema=new XElement("PSSGFILE",new[]{"NeAnimPacket_L4","NeAnimPacket_S4","NeAnimPacket_B1","NeAnimSet","NeAnimClip"}.Select(name=>
            new XElement("node",new XAttribute("name",name),new XAttribute("dataType","None"),new XAttribute("elementsPerRow",16),new XAttribute("linkAttributeName",""),
                new XElement("attribute",new XAttribute("name","id"),new XAttribute("dataType","String")))));
        using(var memory=new MemoryStream()){new XDocument(schema).Save(memory);memory.Position=0;EgoEngineLibrary.Graphics.Pssg.PssgSchema.LoadSchema(memory);}
        var result=new XDocument(race);
        XElement Library(string type)=>result.Descendants("LIBRARY").Single(n=>(string?)n.Attribute("type")==type);
        var packets=Library("NeAnimPacket");var clips=Library("NeAnimClip");var set=result.Descendants("NeAnimSet").Single();
        foreach(string name in Clips) {
            if(clips.Elements().Any(n=>(string?)n.Attribute("id")==name))throw new InvalidDataException("Staff clip already exists.");
            var clip=new XElement(frontend.Descendants("NeAnimClip").Single(n=>(string?)n.Attribute("id")==name));
            foreach(var reference in clip.Elements("NeAnimClipPacketRef").ToArray()) {
                string old=Reference((string)reference.Attribute("packet")!),id="aspen_staff_"+old;
                var sourcePacket=frontend.Descendants().Single(n=>(string?)n.Attribute("id")==old);
                if(!includePropChannels && sourcePacket.Name=="NeAnimPacket_B1"){reference.Remove();continue;}
                if(!packets.Elements().Any(n=>(string?)n.Attribute("id")==id)) {
                    var packet=new XElement(sourcePacket);
                    packet.SetAttributeValue("id",id);packets.Add(packet);
                }
                reference.SetAttributeValue("packet",ReferenceBytes(id));
            }
            var packetCount=new byte[4];BinaryPrimitives.WriteInt32BigEndian(packetCount,clip.Elements("NeAnimClipPacketRef").Count());clip.SetAttributeValue("packetCount",Hex(packetCount));
            clips.Add(clip);set.Add(new XElement("NeAnimSetClipRef",new XAttribute("clip",ReferenceBytes(name))));
        }
        var count=new byte[4];BinaryPrimitives.WriteInt32BigEndian(count,set.Elements().Count());set.SetAttributeValue("clipCount",Hex(count));
        return result;
    }
    // Diagnostic only: this combined bundle passes structural checks but did
    // not reach a rendered scene. Keep it out of the normal conversion.
    internal static XDocument MergeProps(XDocument race,XDocument frontend) {
        var result=new XDocument(race);var imported=new XDocument(frontend);
        var root=imported.Descendants("ROOTNODE").Single();
        var namedProps=root.Elements().Where(n=>n.Attribute("id") is not null).Select(n=>(string)n.Attribute("id")!).ToHashSet(StringComparer.Ordinal);
        var shaderIds=imported.Descendants("SHADERGROUP").Attributes("id").Select(a=>a.Value).ToHashSet(StringComparer.Ordinal);
        var ids=imported.Descendants().Attributes("id").ToDictionary(a=>a.Value,a=>namedProps.Contains(a.Value)||shaderIds.Contains(a.Value)?a.Value:"aspen_staff_prop_"+a.Value,StringComparer.Ordinal);
        foreach(var a in imported.Descendants().Attributes()) {
            if(a.Name=="id")a.Value=ids[a.Value];
            else if(a.Value.StartsWith('#') && ids.TryGetValue(a.Value[1..],out string? target))a.Value="#"+target;
        }
        foreach(var library in imported.Descendants("LIBRARY")) {
            var target=result.Descendants("LIBRARY").Single(n=>(string?)n.Attribute("type")== (string?)library.Attribute("type"));
            if((string?)library.Attribute("type")=="NODE")target.Elements("ROOTNODE").Single().Add(root.Elements().Where(n=>n.Attribute("id") is not null).Select(n=>new XElement(n)));
            else if((string?)library.Attribute("type")=="SHADERGROUP")foreach(var shader in library.Elements()) {
                var existing=target.Elements().SingleOrDefault(n=>(string?)n.Attribute("id")== (string?)shader.Attribute("id"));
                if(existing is null)target.Add(new XElement(shader));
                else if(!XNode.DeepEquals(existing,shader))throw new InvalidDataException("Conflicting staff prop shader.");
            }
            else target.Add(library.Elements().Select(n=>new XElement(n)));
        }
        var all=result.Descendants().Attributes("id").Select(a=>a.Value).ToArray();
        if(all.Distinct(StringComparer.Ordinal).Count()!=all.Length)throw new InvalidDataException("Duplicate merged prop ID.");
        return result;
    }
    internal static void Build(string track,string game,bool includeProps=true,bool includePropChannels=true,Dictionary<string,string>? fingerprints=null,bool combinedProps=false,AspenLayout? layout=null) {
        string crowdPath=$"/data/tracks/usa/{(layout ?? AspenLayout.Lakeside).DirectoryName}/aspen-crowd/";
        foreach(string name in new[]{"crowd_anims.pssg","fe_anims.pssg","staff.pssg","props.pssg","fe_props.pssg"}) {
            string input=Path.Combine(game,"anims",name);PortFiles.NoLinks(input);
            if(fingerprints is not null)fingerprints[input]=PortFiles.Hash(input);
        }
        var bank=Merge(PortFiles.ReadPssg(Path.Combine(game,"anims/crowd_anims.pssg")),PortFiles.ReadPssg(Path.Combine(game,"anims/fe_anims.pssg")),includePropChannels);
        var directory=Path.Combine(track,"aspen-crowd");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"anims.pssg");PortFiles.WritePssg(bank,path);
        if(!XNode.DeepEquals(bank.Root,PortFiles.ReadPssg(path).Root))throw new InvalidDataException("Staff animation round trip failed.");
        if(includeProps && combinedProps) {
            var props=MergeProps(PortFiles.ReadPssg(Path.Combine(game,"anims/props.pssg")),PortFiles.ReadPssg(Path.Combine(game,"anims/fe_props.pssg")));
            path=Path.Combine(directory,"props.pssg");PortFiles.WritePssg(props,path);
            if(!XNode.DeepEquals(props.Root,PortFiles.ReadPssg(path).Root))throw new InvalidDataException("Staff prop round trip failed.");
        }
        path=Path.Combine(track,"route_0/organism_track_dataset.xml");var dataset=PortFiles.ReadXml(path);
        dataset.Descendants("pssg").Single(n=>(string?)n.Attribute("userdata")=="hla").SetAttributeValue("filename",crowdPath+"anims.pssg");
        foreach(var pair in new[]{("/data/anims/staff.pssg","staff"),(combinedProps?crowdPath+"props.pssg":"/data/anims/fe_props.pssg","props")})
            if(includeProps || pair.Item2!="props")dataset.Root!.Add(new XElement("pssg",new XAttribute("processor","OrganismPlugin"),new XAttribute("filename",pair.Item1),new XAttribute("userdata",pair.Item2)));
        using(var writer=new StreamWriter(path,false,new UTF8Encoding(false)))dataset.Save(writer);
    }
}
