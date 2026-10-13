using System.Numerics;
using System.Security.Cryptography;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using DiRT2VR.CarImport;
using DiRT2VR.CustomTracks;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using System.Drawing;

namespace DiRT2VR.CustomCars;

[System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
internal static class FerrariAmbientShadow
{
    // Original supported F2004 images. RGB is shadow opacity; native AO uses
    // white for unoccluded ground. Keep source files immutable and fingerprinted.
    static readonly string[] Names=["body_shadow.png","tyre_0_shadow.png","tyre_1_shadow.png","tyre_2_shadow.png","tyre_3_shadow.png"];
    static readonly string[] Hashes=["C19551A39C517100A948C9BC48EB435B05B8486D6A1053B9E9C9116D5C9C862C","7D5EEF5F95A7906F87D30DC1115203C086DF57B026608AF208E3FB549B112DA3","F35936D2AFB4CF0605391C7AC2A83534BCFB880CA7BE6021B5235AB46064A153","65519CAE05CF8E11809DB4676F0C5E44F26EEA692291F18C30640865A0000DA2","65519CAE05CF8E11809DB4676F0C5E44F26EEA692291F18C30640865A0000DA2"];
    internal static void Build(string source,string donor,string output)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Ferrari shadow conversion requires Windows.");
        byte[] Read(int i) {
            var bytes=File.ReadAllBytes(Path.Combine(source,Names[i]));
            if(Convert.ToHexString(SHA256.HashData(bytes))!=Hashes[i])throw new IOException("Unsupported source shadow: "+Names[i]);
            return bytes;
        }
        var streams=new List<MemoryStream>();var images=new List<Bitmap>();
        try {
            for(int i=0;i<5;i++){streams.Add(new MemoryStream(Read(i),false));images.Add(new Bitmap(streams[^1]));}
            if(images[0].Width!=512 || images[0].Height!=512 || images.Skip(1).Any(i=>i.Width!=128 || i.Height!=128))throw new IOException("Unsupported source shadow dimensions");
            var model=CarKn5.Read(Path.Combine(source,"ferrari_f2004.kn5"));var wheels=new Dictionary<string,Vector3>();
            void Visit(CarNode node,Matrix4x4 world) {
                world=node.LocalTransform*world;
                if(node.Name.StartsWith("WHEEL_",StringComparison.Ordinal))wheels.Add(node.Name,world.Translation);
                foreach(var child in node.Children)Visit(child,world);
            }
            Visit(model.Root,Matrix4x4.Identity);
            string[] sides=["LF","RF","LR","RR"];
            if(wheels.Count!=4 || sides.Any(s=>!wheels.ContainsKey("WHEEL_"+s)))throw new IOException("Supported source wheel pivots required");
            float Opacity(Bitmap image,float u,float v) {
                if(u<0 || u>1 || v<0 || v>1)return 0;
                float x=u*(image.Width-1),y=v*(image.Height-1);int ix=(int)x,iy=(int)y;
                float a=image.GetPixel(ix,iy).R,b=image.GetPixel(Math.Min(ix+1,image.Width-1),iy).R;
                float c=image.GetPixel(ix,Math.Min(iy+1,image.Height-1)).R,d=image.GetPixel(Math.Min(ix+1,image.Width-1),Math.Min(iy+1,image.Height-1)).R;
                return ((a+(b-a)*(x-ix))*(1-(y-iy))+(c+(d-c)*(x-ix))*(y-iy))/255;
            }
            const int size=256;var pixels=new byte[size*size*4];
            // AC ambient_shadows.ini WIDTH=1.0/LENGTH=2.8 are half extents.
            // Tyre contact patches use source widths/radius with a soft margin.
            var min=new Vector3(-1.2f,0,-3f);var max=new Vector3(1.2f,0,3f);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
                float px=min.X+(x+.5f)/size*(max.X-min.X),pz=max.Z-(y+.5f)/size*(max.Z-min.Z);
                float transmission=1-Opacity(images[0],(px+1)/2,(2.8f-pz)/5.6f);
                for(int i=0;i<4;i++) {
                    var pivot=wheels["WHEEL_"+sides[i]];float halfWidth=((i<2?.245f:.325f)+.12f)/2,halfLength=.39f;
                    transmission*=1-Opacity(images[i+1],.5f+(px-pivot.X)/(2*halfWidth),.5f-(pz-pivot.Z)/(2*halfLength));
                }
                byte value=(byte)Math.Clamp((int)MathF.Round(transmission*255),0,255);int offset=(y*size+x)*4;
                pixels[offset]=pixels[offset+1]=pixels[offset+2]=value;pixels[offset+3]=255;
            }
            using var input=File.OpenRead(Path.Combine(donor,"cars/prc/prc_AO.pssg"));var file=PssgFile.Open(input);
            var texture=file.Elements<PssgTexture>().Single();var bounds=file.Elements<PssgBoundingBox>().Single();
            var encoder=new BcEncoder(CompressionFormat.Bc1);encoder.OutputOptions.GenerateMipMaps=true;
            using var encoded=new MemoryStream();encoder.EncodeToDds(pixels,size,size,PixelFormat.Bgra32).Write(encoded);encoded.Position=0;
            new DdsFile(encoded).ToPssgElement(texture);texture.Id="ao_f04.tga";texture.AutoMipMap=false;
            bounds.BoundsMin=min;bounds.BoundsMax=max;
            var target=Path.Combine(output,"f04_AO.pssg");using(var stream=File.Create(target))file.WritePssg(stream);
            for(int i=0;i<5;i++)Read(i); // Verify sources stayed unchanged during conversion.
            SafeFiles.WriteJson(Path.Combine(output,"ambient-shadow-build.json"),new {
                Sources=Names.Select((name,i)=>new{Path=name,Sha256=Hashes[i]}),SourceWheelPivots=wheels.Select(w=>new{Side=w.Key,X=w.Value.X,Z=w.Value.Z}),
                BoundsMin=new[]{min.X,min.Y,min.Z},BoundsMax=new[]{max.X,max.Y,max.Z},
                NativeOutputSha256=SafeFiles.Hash(target),NativeRuntimeValidated=false,
                Limitations=new[]{"Static source-derived contact AO; tyre patch size is approximate","Dynamic cast shadows remain native"}
            });
        } finally {foreach(var image in images)image.Dispose();foreach(var stream in streams)stream.Dispose();}
    }
}