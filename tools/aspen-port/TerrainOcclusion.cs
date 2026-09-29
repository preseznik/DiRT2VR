using System.Xml.Linq;
using BCnEncoder.Decoder;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// DiRT 3: R=AO, B=specular AO, A=baked shadow.
// DiRT 2: R=baked shadow, G=AO, B=diffuse multiplier.
internal static class TerrainOcclusion
{
    internal static object Convert(string track)
    {
        var references=new HashSet<string>(StringComparer.Ordinal);
        foreach(var relative in new[] { "land.pssg", "route_0/routesplit.pssg" })
        {
            var doc=PortFiles.ReadPssg(Path.Combine(track,relative));
            var groups=doc.Descendants("SHADERGROUP").ToDictionary(g=>"#"+(string)g.Attribute("id")!);
            foreach(var material in doc.Descendants("SHADERINSTANCE"))
            {
                var group=(string)material.Attribute("shaderGroup")!;
                if(!group.StartsWith("#terrain_",StringComparison.Ordinal)) continue;
                int index=Array.FindIndex(groups[group].Elements("SHADERINPUTDEFINITION").ToArray(),e=>(string?)e.Attribute("name")=="TAmbientOcclusion");
                var reference=(string?)material.Elements("SHADERINPUT").SingleOrDefault(e=>(int)e.Attribute("parameterID")! == index)?.Attribute("texture");
                if(reference is null) continue;
                if(!reference.StartsWith("tracksplit.pssg#",StringComparison.Ordinal)) throw new InvalidDataException("Unexpected terrain AO owner: "+reference);
                references.Add(reference["tracksplit.pssg#".Length..]);
            }
        }
        var path=Path.Combine(track,"tracksplit.pssg");
        using var stream=PortFiles.OpenRead(path); var file=PssgFile.Open(stream);
        var document=PortFiles.ReadPssg(path);
        var report=new List<object>();
        foreach(var id in references.Order())
        {
            var source=file.GetObject<PssgTexture>(id.AsMemory());
            using var ddsStream=new MemoryStream(); source.ToDdsFile().Write(ddsStream); ddsStream.Position=0;
            var dds=BCnEncoder.Shared.ImageFiles.DdsFile.Load(ddsStream);
            var levels=new BcDecoder().DecodeAllMipMaps(dds);
            using var pixels=new MemoryStream();
            foreach(var level in levels)
                foreach(var pixel in level)
                {
                    // BGRA bytes for DiRT 2's ui8x4, retaining every decoded mip.
                    pixels.WriteByte(255); pixels.WriteByte(pixel.r); pixels.WriteByte(pixel.a); pixels.WriteByte(255);
                }
            var target=document.Descendants("TEXTURE").Single(t=>(string?)t.Attribute("id")==id);
            if((string?)target.Attribute("texelFormat")!="dxt5") throw new InvalidDataException("Expected DiRT 3 BC3 terrain AO: "+id);
            var block=target.Elements("TEXTUREIMAGEBLOCK").Single();
            var bytes=pixels.ToArray(); target.SetAttributeValue("texelFormat","ui8x4");
            block.SetAttributeValue("size",bytes.Length);
            block.Element("TEXTUREIMAGEBLOCKDATA")!.Value=EgoEngineLibrary.Helper.HexHelper.ByteArrayToHexViaLookup32(bytes);
            report.Add(new { Texture=id, Width=source.Width, Height=source.Height, MipLevels=levels.Length, Bytes=bytes.Length });
        }
        stream.Dispose();
        PortFiles.WritePssg(document,path+".tmp");
        var saved=PortFiles.ReadPssg(path+".tmp");
        foreach(var doc in new[]{document,saved})
            foreach(var data in doc.Descendants("TEXTUREIMAGEBLOCKDATA"))
                data.Value=string.Concat(data.Value.Where(c=>!char.IsWhiteSpace(c)));
        if(!XNode.DeepEquals(document,saved)) throw new InvalidDataException("Terrain AO changed during serialization.");
        File.Move(path+".tmp",path,true);
        return new { Mapping="R=source alpha; G=source red; B=1; A=1", Textures=report, ColourMapSupported=false };
    }
}
