using System.Globalization;
using System.Xml.Linq;

internal static class AmbientEffects
{
    internal static readonly string[] Names=["e_birds_glide","e_birds_startle","E_Bridge_SnowGust","E_Smoke_chimney","E_Snow_Floaty_Roof","E_Tree_AmbientSnow_Single"];
    const string Prefix="D2VR_Aspen_Ambient_";
    static string F(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
    static double[] Numbers(string text)=>text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
    internal sealed record Curve(double[] Times,double[] Values,double Variance);
    internal static Curve ReadCurve(XElement node) {
        var f=Numbers((string)node.Attribute("curve")!);
        if(f.Length==0)throw new InvalidDataException("Empty ambient scalar curve.");
        int count=(int)f[0];
        if(count<1 || count>32 || count!=f[0] || f.Length!=2*count+6 || f.Any(v=>!double.IsFinite(v)))throw new InvalidDataException("Unsupported ambient scalar curve.");
        var times=f.Skip(1).Take(count).ToArray();
        if(times.Any(t=>t<0||t>1) || !times.SequenceEqual(times.Order()))throw new InvalidDataException("Invalid ambient curve times.");
        return new(times,f.Skip(1+count).Take(count).ToArray(),f[1+2*count]);
    }
    internal static XDocument ConvertPlacements(XDocument source) {
        var result=new XDocument(source);
        foreach(var instance in result.Root!.Elements("instance").ToArray()) {
            if((string?)instance.Attribute("lightingType")=="nightonly"){instance.Remove();continue;}
            if(Names.Contains((string?)instance.Attribute("effectName")))instance.SetAttributeValue("effectName",Prefix+(string)instance.Attribute("effectName")!);
        }
        return result;
    }
    static XElement CopyFields(XElement source,string fields)=>new(source.Name,fields.Split(' ').Where(n=>source.Attribute(n) is not null).Select(n=>new XAttribute(n,(string)source.Attribute(n)!)));
    internal static XElement ConvertEmitter(XElement source,int id,IReadOnlyDictionary<string,string> textures) {
        var model=source.Element("model")!;var material=source.Element("material")!;var p=source.Element("particleProperties")!;
        if((string?)model.Attribute("geometryType")!="sprite")throw new InvalidDataException("Ambient emitter is not a sprite.");
        var result=new XElement("emitter",new XAttribute("id",id),new XAttribute("name",Prefix+(string)source.Attribute("name")!),CopyFields(model,"geometryType particleType billboardingType"));
        string type=((string)material.Attribute("type")!).ToLowerInvariant();
        var convertedMaterial=new XElement("material",new XAttribute("textureType",type));
        string texture=textures[(string)material.Element("texture")!.Attribute("name")!];
        if(type=="static")convertedMaterial.Add(new XAttribute("textureName",texture));
        else if(type is "dynamic" or "frequency") {
            convertedMaterial.Add(new XElement("materialtexture",new XAttribute("filename",texture),new XAttribute("numFrames",(string)material.Attribute("frames")!),new XAttribute("widthInBlocks",(string)material.Attribute("columns")!),new XAttribute("heightInBlocks",(string)material.Attribute("rows")!)));
            convertedMaterial.Add(type=="frequency"?new XElement("properties",new XAttribute("frequencies",(string)material.Attribute("frequencies")!)):
                new XElement("properties",new XAttribute("type",(string)material.Attribute("dyntype")!),new XAttribute("rate",(string)material.Attribute("rate")!),new XAttribute("loop",(string)material.Attribute("loop")!)));
        } else throw new InvalidDataException("Unsupported ambient material type.");
        result.Add(convertedMaterial,CopyFields(source.Element("physics")!,"collisiondetection fullphysics bounce heightscale"));
        foreach(var force in source.Elements("force")) {
            string kind=(string)force.Attribute("type")!;
            if(kind=="rotationalwind"){result.Add(new XElement("force",new XAttribute("type",kind)));continue;}
            if(kind is not ("gravityCurve" or "windCurve" or "dragCurve" or "angulardragCurve"))throw new InvalidDataException("Unsupported ambient force.");
            var curve=ReadCurve(force);double value=curve.Values.Average();
            var converted=new XElement("force",new XAttribute("type",kind[..^5]),new XAttribute("influence",F(kind is "dragCurve" or "angulardragCurve"?100:value*100)));
            if(kind is "dragCurve" or "angulardragCurve")converted.Add(new XAttribute("rangeX",F(value)));
            result.Add(converted);
        }
        var shape=CopyFields(source.Element("emitterShape")!,"shape location length width height diameter");
        foreach(var name in new[]{"shape","location"})shape.Attribute(name)!.Value=shape.Attribute(name)!.Value.ToLowerInvariant();
        result.Add(shape);
        var spawn=CopyFields(source.Element("spawn")!,"rate number finishWhenStopped randomPointEmission");
        if(double.Parse((string?)spawn.Attribute("rate")??"0",CultureInfo.InvariantCulture)>0)spawn.Attribute("number")?.Remove();else spawn.Attribute("rate")?.Remove();
        result.Add(spawn);
        var properties=new XElement("particleProperties",CopyFields(p.Element("life")!,"life variance"),CopyFields(p.Element("speed")!,"speed variation direction divergence divergenceY inheritamount randommotion velocityturbulence velocityagressiveness"));
        var spin=CopyFields(p.Element("spin")!,"rate variance axisType axis initialRotation initialRotationVariance");spin.SetAttributeValue("axisType","ParticleSpace");properties.Add(spin);
        var width=ReadCurve(p.Element("widthCurve")!);var height=ReadCurve(p.Element("heightCurve")!);var alpha=ReadCurve(p.Element("alphaCurve")!);
        properties.Add(new XElement("agescale",new XAttribute("initialWidth",F(width.Values[0])),new XAttribute("initialHeight",F(height.Values[0])),new XAttribute("finalWidth",F(width.Values[^1])),new XAttribute("finalHeight",F(height.Values[^1])),new XAttribute("scaleVariance",F(Math.Max(width.Variance,height.Variance)*100)),new XAttribute("scaleType","linear")));
        properties.Add(new XElement("alpha",new XAttribute("start",F(alpha.Values[0])),new XAttribute("middle",F(alpha.Values.Max())),new XAttribute("end","0"),
            new XAttribute("inTime",F(alpha.Times.Length>1?alpha.Times[1]:.1)),new XAttribute("outTime",F(alpha.Times.Length>2?alpha.Times[^2]:.8)),
            new XAttribute("erodePercent",0),new XAttribute("depthFadeDistance",(string?)p.Element("alpha")?.Attribute("depthFadeDistance")??"0")));
        var colour=Numbers((string)p.Element("colour")!.Attribute("curve")!);int n=(int)colour[0];
        if(n<1 || colour.Length<1+5*n || colour.Any(v=>!double.IsFinite(v)))throw new InvalidDataException("Unsupported ambient colour curve.");
        string Colour(int sample)=>string.Join(' ',colour.Skip(1+2*n+sample*3).Take(3).Select(F));
        properties.Add(new XElement("colour",new XAttribute("start",Colour(0)),new XAttribute("end",Colour(n-1))));
        result.Add(properties);return result;
    }
    internal static void Build(string track,string d3,string session,Dictionary<string,string>? inputs=null,AspenLayout? layout=null) {
        layout ??= AspenLayout.Lakeside;
        XDocument Read(string path){if(inputs is not null)inputs[path]=PortFiles.Hash(path);return PortFiles.ReadXml(path);}
        var systems=Read(Path.Combine(d3,"effects/particle_systems/effects_export.xml"));
        var selected=Names.Select(name=>systems.Descendants("System").Single(s=>(string?)s.Attribute("name")==name)).ToArray();
        var ids=selected.SelectMany(s=>s.Descendants().Attributes("emitterID")).Select(a=>a.Value).ToHashSet();
        var sourceEmitters=Directory.EnumerateFiles(Path.Combine(d3,"effects/particle_systems"),"emitters_export_*.xml").Order().SelectMany(p=>Read(p).Descendants("emitter")).Where(e=>ids.Contains((string?)e.Attribute("id")??"")).ToArray();
        if(sourceEmitters.Length!=ids.Count)throw new InvalidDataException("Missing ambient emitter.");
        var names=sourceEmitters.SelectMany(e=>e.Descendants("texture")).Select(t=>(string)t.Attribute("name")!).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(s=>s,s=>"d2vr_aspen_ambient_"+s.ToLowerInvariant(),StringComparer.OrdinalIgnoreCase);
        XDocument? textureBundle=null;var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string name in new[]{"pfx_textures.pssg","pfx_snow_textures.pssg"}) {
            var path=Path.Combine(d3,"effects/textures",name);if(inputs is not null)inputs[path]=PortFiles.Hash(path);
            var doc=PortFiles.ReadPssg(path);
            if(textureBundle is null){textureBundle=new XDocument(doc);textureBundle.Descendants("TEXTURE").Remove();}
            foreach(var t in doc.Descendants("TEXTURE"))if(names.TryGetValue((string)t.Attribute("id")!,out var renamed)) {
                if(!found.Add((string)t.Attribute("id")!))throw new InvalidDataException("Duplicate ambient texture.");
                var copy=new XElement(t);copy.SetAttributeValue("id",renamed);textureBundle.Root!.Element("PSSGDATABASE")!.Add(copy);
            }
        }
        if(!found.SetEquals(names.Keys))throw new InvalidDataException("Missing ambient texture.");
        var idMap=sourceEmitters.Select((e,i)=>(Old:(string)e.Attribute("id")!,New:21000+i)).ToDictionary(p=>p.Old,p=>p.New);
        var particles=new XDocument(new XElement("ParticleData",new XElement("EmitterLibrary",sourceEmitters.Select(e=>ConvertEmitter(e,idMap[(string)e.Attribute("id")!],names))),new XElement("SystemLibrary",selected.Select((source,i)=>{
            var s=new XElement(source);s.SetAttributeValue("name",Prefix+(string)source.Attribute("name")!);s.SetAttributeValue("id",22000+i);
            foreach(var birth in s.Descendants("birth")) {
                string old=(string)birth.Attribute("emitterID")!;
                var emitter=sourceEmitters.Single(e=>(string?)e.Attribute("id")==old);
                // D2 stops finite births during the intro. Keep continuous sources
                // alive while their original placement trigger remains active.
                if(double.Parse((string?)emitter.Element("spawn")?.Attribute("rate")??"0",CultureInfo.InvariantCulture)>0)
                    birth.SetAttributeValue("end","-1");
                birth.SetAttributeValue("emitterID",idMap[old]);
            }
            return s;
        }))));
        var folder=Path.Combine(track,"aspen-effects");Directory.CreateDirectory(folder);
        PortFiles.WriteXml(particles,Path.Combine(folder,"ambient.xml"),EgoEngineLibrary.Xml.XmlType.BinXml);
        PortFiles.WritePssg(textureBundle!,Path.Combine(folder,"ambient-textures.pssg"));
        var originalPlacements=Read(Path.Combine(d3,"tracks/locations/usa/aspen",layout.SourceRoute,"pfx.xml"));
        var placements=ConvertPlacements(originalPlacements);
        int night=originalPlacements.Root!.Elements().Count()-placements.Root!.Elements().Count();
        int restored=placements.Root.Elements("instance").Count(n=>((string?)n.Attribute("effectName"))?.StartsWith(Prefix,StringComparison.Ordinal)==true);
        var emitterIds=particles.Descendants("emitter").Select(e=>(string)e.Attribute("id")!).ToHashSet();
        if(!particles.Descendants("birth").All(b=>emitterIds.Contains((string)b.Attribute("emitterID")!)))throw new InvalidDataException("Unresolved ambient birth.");
        var textureIds=textureBundle!.Descendants("TEXTURE").Select(t=>(string)t.Attribute("id")!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!particles.Descendants("material").All(m=>textureIds.Contains((string?)m.Attribute("textureName")??(string)m.Element("materialtexture")!.Attribute("filename")!)))throw new InvalidDataException("Unresolved ambient texture.");
        var pfx=Path.Combine(track,"route_0/pfx.xml");PortFiles.WriteXml(placements,pfx+".tmp",EgoEngineLibrary.Xml.XmlType.BinXml);File.Move(pfx+".tmp",pfx,true);
        var manifestPath=Path.Combine(session,"effects.json");var manifest=System.Text.Json.JsonSerializer.Deserialize<EffectsTransaction.Manifest>(File.ReadAllBytes(manifestPath))!;
        var entries=new List<EffectsTransaction.Entry>();
        foreach(var entry in manifest.Files) {
            var path=Path.Combine(session,entry.Path);
            if(PortFiles.Hash(path)!=entry.AppliedHash)throw new InvalidDataException("Effects registration changed.");
            var doc=PortFiles.ReadXml(path);bool xml=entry.Path.EndsWith("pfx_kickup_data_set.xml",StringComparison.Ordinal);
            doc.Root!.Add(xml?new XElement("xml",new XAttribute("processor","ParticleManagerPlugin"),new XAttribute("filename",layout.EffectsPath+"ambient.xml"),new XAttribute("map","RENDER"),new XAttribute("pool","TEMPORARY"),new XAttribute("userdata","pfxeffect")):
                new XElement("pssg",new XAttribute("processor","ParticleManagerPlugin"),new XAttribute("filename",layout.EffectsPath+"ambient-textures.pssg"),new XAttribute("userdata","textures")));
            using(var writer=new StreamWriter(path,false,new System.Text.UTF8Encoding(false)))doc.Save(writer);
            entries.Add(entry with{AppliedHash=PortFiles.Hash(path)});
        }
        PortFiles.Json(manifestPath,new EffectsTransaction.Manifest(1,entries.ToArray()));
        PortFiles.Json(Path.Combine(Path.GetDirectoryName(track)!,"ambient-effects.json"),new{Systems=selected.Length,Emitters=sourceEmitters.Length,Textures=found.Count,RestoredPlacements=restored,ExcludedNightPlacements=night,
            UnresolvedSystems=new[]{"E_Tree_Section_Snow","E_Snow_Floaty"},Approximation="Size and colour endpoints; normalized alpha envelope; average forces; no erosion or source lighting curves. Continuous emitters remain active while triggered; burst emitters retain number.",RuntimeValidated=false});
    }
    internal static void Audit(string d3,string d2,string output) {
        PortFiles.NewOutput(output,d3,d2);
        var systems=PortFiles.ReadXml(Path.Combine(d3,"effects/particle_systems/effects_export.xml"));
        var selected=systems.Descendants("System").Where(n=>Names.Contains((string?)n.Attribute("name"))).ToArray();
        var ids=selected.SelectMany(n=>n.Descendants().Attributes("emitterID")).Select(a=>a.Value).ToHashSet();
        var emitters=Directory.EnumerateFiles(Path.Combine(d3,"effects/particle_systems"),"emitters_export_*.xml").SelectMany(p=>PortFiles.ReadXml(p).Descendants("emitter")).Where(n=>ids.Contains((string?)n.Attribute("id") ?? "")).ToArray();
        new XDocument(new XElement("ParticleData",new XElement("EmitterLibrary",emitters),new XElement("SystemLibrary",selected))).Save(Path.Combine(output,"source-selected.xml"));
        var d2docs=Directory.EnumerateFiles(Path.Combine(d2,"effects/particle_systems"),"*.xml").Select(PortFiles.ReadXml).ToArray();
        PortFiles.Json(Path.Combine(output,"target-schema.json"),new {
            Materials=d2docs.SelectMany(d=>d.Descendants("material")).Select(n=>n.ToString(SaveOptions.DisableFormatting)).Distinct().ToArray(),
            Forces=d2docs.SelectMany(d=>d.Descendants("force")).GroupBy(n=>(string?)n.Attribute("type")).Select(g=>new{Type=g.Key,Example=g.First().ToString(SaveOptions.DisableFormatting)}).ToArray(),
            Shapes=d2docs.SelectMany(d=>d.Descendants("emitterShape")).GroupBy(n=>(string?)n.Attribute("shape")).Select(g=>new{Type=g.Key,Example=g.First().ToString(SaveOptions.DisableFormatting)}).ToArray()
        });
        var wanted=emitters.SelectMany(e=>e.Descendants("texture")).Select(n=>(string)n.Attribute("name")!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var textures=new List<object>();
        foreach(var path in Directory.EnumerateFiles(Path.Combine(d3,"effects/textures"),"*.pssg")) {
            var doc=PortFiles.ReadPssg(path);
            foreach(var t in doc.Descendants("TEXTURE").Where(n=>wanted.Contains((string?)n.Attribute("id") ?? "")))textures.Add(new{Name=(string)t.Attribute("id")!,Path=path});
        }
        PortFiles.Json(Path.Combine(output,"textures.json"),textures);
        Console.WriteLine("Ambient audit: "+output);
    }
}
