using System.Buffers.Binary;
using System.Globalization;
using System.Xml.Linq;
using BCnEncoder.Decoder;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// DiRT 3 multiplies road albedo by a scaled colour atlas. DiRT 2 has no such
// input, but its AO blue channel supports a scalar diffuse multiplier. Restore
// the near-neutral atlas detail there, limited to Buttermilk's local candidate.
internal static class ButtermilkRoadColour
{
    internal static object Convert(string track,string source,string route)
    {
        string path=Path.Combine(track,"tracksplit.pssg");
        var resources=PortFiles.ReadPssg(path);
        var textures=resources.Descendants("TEXTURE").ToDictionary(t=>(string)t.Attribute("id")!);
        var metadata=PortFiles.ReadPssg(Path.Combine(source,"tracksplit.pssg")).Descendants("PNTEXTURESCALING").ToDictionary(t=>"#"+(string)t.Attribute("id")!);
        using var input=PortFiles.OpenRead(path);var file=PssgFile.Open(input);
        var masks=new Dictionary<(string Ao,string Colour,float Scale),string>();
        var reports=new List<object>();int bindings=0;
        string? Texture(XElement material,string[] names,string name)
            =>(string?)material.Elements("SHADERINPUT").SingleOrDefault(i=>names[(int)i.Attribute("parameterID")!]==name)?.Attribute("texture");
        float[] Floats(XElement node,string attribute)
        {
            var bytes=System.Convert.FromHexString(string.Concat(((string)node.Attribute(attribute)!).Where(c=>!char.IsWhiteSpace(c))));
            if(bytes.Length!=16) throw new InvalidDataException("Unexpected terrain colour calibration.");
            var values=Enumerable.Range(0,4).Select(i=>BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(i*4))).ToArray();
            if(values.Any(v=>!float.IsFinite(v))) throw new InvalidDataException("Nonfinite terrain colour calibration.");
            return values;
        }
        foreach(string relative in new[]{"tracksplit.pssg","land.pssg","route_0/routesplit.pssg"}) {
            var original=PortFiles.ReadPssg(Path.Combine(source,relative.StartsWith("route_0/",StringComparison.Ordinal)?route+"/routesplit.pssg":"tracksplit.pssg"));
            var definitions=original.Descendants("SHADERGROUP").ToDictionary(g=>"#"+(string)g.Attribute("id")!,g=>g.Elements("SHADERINPUTDEFINITION").Select(n=>(string)n.Attribute("name")!).ToArray());
            var target=relative=="tracksplit.pssg"?resources:PortFiles.ReadPssg(Path.Combine(track,relative));
            var materials=target.Descendants("SHADERINSTANCE").ToDictionary(m=>(string)m.Attribute("id")!);
            var targetGroups=target.Descendants("SHADERGROUP").ToDictionary(g=>"#"+(string)g.Attribute("id")!,g=>g.Elements("SHADERINPUTDEFINITION").Select(n=>(string)n.Attribute("name")!).ToArray());
            foreach(var material in original.Descendants("SHADERINSTANCE").Where(m=>(string?)m.Attribute("shaderGroup")=="#terrain_road.fx")) {
                var names=definitions["#terrain_road.fx"];
                string? colour=Texture(material,names,"TColourMap");
                if(colour is null) continue;
                string colourId=colour[(colour.IndexOf('#')+1)..];
                if(!Enumerable.Range(1,8).Any(i=>colourId==$"asp_rx_col_{i:00}_d.tga")) continue; // Leave bridge/metal and landscape materials alone.
                var dest=materials[(string)material.Attribute("id")!];
                var destNames=targetGroups[(string)dest.Attribute("shaderGroup")!];
                var aoInput=dest.Elements("SHADERINPUT").Single(i=>destNames[(int)i.Attribute("parameterID")!]=="TAmbientOcclusion");
                string reference=(string)aoInput.Attribute("texture")!;
                string ao=reference[(reference.IndexOf('#')+1)..];
                var scaleInput=material.Elements("SHADERINPUT").Single(i=>names[(int)i.Attribute("parameterID")!]=="ColourMapScale");
                float scale=float.Parse(scaleInput.Value,CultureInfo.InvariantCulture);
                if(!float.IsFinite(scale) || scale<=0 || scale>4) throw new InvalidDataException("Unexpected road colour multiplier.");
                var key=(ao,colourId,scale);
                if(!masks.TryGetValue(key,out string? id)) {
                    var aoTexture=textures[ao];var colourTexture=textures[colourId];
                    if((string?)aoTexture.Attribute("texelFormat")!="ui8x4" || new[]{"width","height","numberMipMapLevels"}.Any(a=>(string?)aoTexture.Attribute(a)!=(string?)colourTexture.Attribute(a)))
                        throw new InvalidDataException("Road colour and AO atlas layouts differ: "+ao+" / "+colourId);
                    var calibration=metadata[(string)colourTexture.Element("USERDATA")!.Attribute("object")!];
                    var channelScale=Floats(calibration,"scale");var channelMin=Floats(calibration,"min");
                    using var ddsStream=new MemoryStream();file.GetObject<PssgTexture>(colourId.AsMemory()).ToDdsFile().Write(ddsStream);ddsStream.Position=0;
                    var levels=new BcDecoder().DecodeAllMipMaps(BCnEncoder.Shared.ImageFiles.DdsFile.Load(ddsStream));
                    var texture=new XElement(aoTexture);id="d2vr_buttermilk_road_ao_"+masks.Count;
                    texture.SetAttributeValue("id",id);texture.Elements("USERDATA").Remove();
                    var data=texture.Descendants("TEXTUREIMAGEBLOCKDATA").Single();
                    var bytes=System.Convert.FromHexString(string.Concat(data.Value.Where(c=>!char.IsWhiteSpace(c))));
                    int offset=0,darkened=0,aboveOne=0;double minimum=double.MaxValue,maximum=0;
                    foreach(var level in levels) foreach(var pixel in level) {
                        double r=(pixel.r/255.0*channelScale[0]+channelMin[0])*scale;
                        double g=(pixel.g/255.0*channelScale[1]+channelMin[1])*scale;
                        double b=(pixel.b/255.0*channelScale[2]+channelMin[2])*scale;
                        if(Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))>.05) throw new InvalidDataException("Road atlas is not near-neutral.");
                        double value=.2126*r+.7152*g+.0722*b;minimum=Math.Min(minimum,value);maximum=Math.Max(maximum,value);
                        if(value>1) aboveOne++;
                        byte before=bytes[offset];bytes[offset]=(byte)Math.Round(before*Math.Clamp(value,0,1));
                        if(bytes[offset]<before) darkened++;
                        offset+=4;
                    }
                    if(offset!=bytes.Length) throw new InvalidDataException("Road atlas mip lengths differ.");
                    data.Value=EgoEngineLibrary.Helper.HexHelper.ByteArrayToHexViaLookup32(bytes);
                    resources.Descendants("TEXTURE").First().Parent!.Add(texture);masks.Add(key,id);
                    reports.Add(new {Texture=id,SourceColour=colourId,SourceAo=ao,Scale=scale,Minimum=minimum,Maximum=maximum,DarkenedTexels=darkened,AboveOneTexels=aboveOne});
                }
                aoInput.SetAttributeValue("texture",(relative=="tracksplit.pssg"?"#":"tracksplit.pssg#")+id);bindings++;
            }
            if(relative!="tracksplit.pssg") Save(target,Path.Combine(track,relative));
        }
        if(masks.Count==0 || bindings==0) throw new InvalidDataException("No Buttermilk road colour atlases.");
        input.Dispose();Save(resources,path);
        return new {Materials=bindings,Atlases=reports,Mapping="AO B *= clamp(luminance((colour * channelScale + channelMin) * ColourMapScale), 0, 1)",
            GeometryAndLightingUnchanged=true,OriginalAoChannelsPreserved=true,RuntimeValidated=false};
    }
    static void Save(XDocument document,string path)
    {
        PortFiles.WritePssg(document,path+".colour-tmp");var saved=PortFiles.ReadPssg(path+".colour-tmp");
        foreach(var doc in new[]{document,saved}) foreach(var data in doc.Descendants("TEXTUREIMAGEBLOCKDATA")) data.Value=string.Concat(data.Value.Where(c=>!char.IsWhiteSpace(c)));
        if(!XNode.DeepEquals(document,saved)) throw new InvalidDataException("Road colour atlas changed during serialization.");
        File.Move(path+".colour-tmp",path,true);
    }
}
