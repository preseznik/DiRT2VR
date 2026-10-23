using System.Globalization;
using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Reuse the player-approved native shader contracts, but populate every
// material from this venue's source inputs. Never copy asphalt texture bindings.
internal sealed class PikesGravelMaterials
{
    readonly Dictionary<string, XElement> groups, templates;
    readonly Dictionary<string, ShaderInputInfo> layouts;
    readonly XDocument lighting;
    internal readonly Texture SunTexture, AmbientTexture;
    readonly BakedLightingProbe.LightMap sun;
    readonly List<object> mappings = [];
    readonly Dictionary<string,(byte[] Scale,byte[] Min)> colourRanges=[];
    internal void RegisterColourRange(string key,EgoEngineLibrary.Graphics.Pssg.PssgFile file,PssgTexture texture) => colourRanges[key]=GroundColourRange.Read(file,texture);
    internal PikesGravelMaterials(string rally, PikesSourceProfile profile)
    {
        var xml = XDocument.Parse(PikesResources.Text("material-contracts.xml"));
        groups = xml.Root!.Elements("SHADERGROUP").ToDictionary(e => (string)e.Attribute("id")!);
        templates = xml.Root.Elements("SHADERINSTANCE").ToDictionary(e => ((string)e.Attribute("shaderGroup")!)[1..]);
        using var savedLayouts=JsonDocument.Parse(PikesResources.Text("vertex-layouts.json"));
        layouts=savedLayouts.RootElement.EnumerateArray().Select(l=>new ShaderInputInfo(l.GetProperty("ShaderGroupId").GetString()!,
            l.GetProperty("BlockInputs").EnumerateArray().Select(b=>new ShaderBlockInputInfo(b.GetProperty("VertexInputs").EnumerateArray()
                .Select(v=>new ShaderVertexInputInfo(v.GetProperty("Name").GetString()!,v.GetProperty("DataType").GetString()!,v.GetProperty("Offset").GetUInt32(),v.GetProperty("Stride").GetUInt32())).ToList())).ToArray()))
            .ToDictionary(l=>l.ShaderGroupId);
        // The accepted colour and shadow foliage draws share one RDS. The
        // library's first-reference scan can report only its shadow alias.
        if(!layouts.ContainsKey("pikes_foliage.fx"))layouts.Add("pikes_foliage.fx",new ShaderInputInfo("pikes_foliage.fx",layouts["pikes_foliage_shadow55.fx"].BlockInputs));
        lighting = profile.Lighting(rally);
        sun = new(profile.PathIn(rally,"route_0/shadow_map_"+profile.Condition+".clm"),false);
        SunTexture = TextureBytes(sun.Decode());
        AmbientTexture = TextureBytes(PikesSponsorBarrier.Read(profile.PathIn(rally,"ambientmaps_"+profile.Condition+".pssg"))
            .GetObject<PssgTexture>("skymapfortrees.tga".AsMemory()).ToDdsFile());
    }
    static Texture TextureBytes(EgoEngineLibrary.Graphics.Dds.DdsFile file)
    {
        using var stream=new MemoryStream();file.Write(stream);var bytes=stream.ToArray();
        return new(bytes,System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    }
    internal static bool Supports(string group) => group.StartsWith("terrain_",StringComparison.Ordinal) || group is "treesheet_foliage_norm.fx" or "facing_foliage.fx";
    static string Kind(string source) => source switch
    {
        "terrain_track_vista_d4.fx" => "terrain_pikes_vista.fx",
        "terrain_road_flat.fx" or "terrain_road_blend_flat_simple.fx" or "terrain_rockbank_flat.fx" => "pikes_bank_flat47.fx",
        "terrain_road.fx" or "terrain_road_blend.fx" => "pikes_bank_road47.fx",
        "terrain_rockbank.fx" => "pikes_bank_rockbank47.fx",
        "terrain_wsm_edge_nm.fx" => "pikes_bank_wsm_edge47.fx",
        "terrain_wsm_blend_nm.fx" => "pikes_bank_wsm_blend47.fx",
        "terrain_edge_nm.fx" => "pikes_bank_edge47.fx",
        "terrain_infield_nm.fx" => "pikes_terrain_infield_nm.fx",
        "treesheet_foliage_norm.fx" or "facing_foliage.fx" => "pikes_foliage.fx",
        _ when !source.StartsWith("terrain_", StringComparison.Ordinal) => "object_simple.fx",
        _ => throw new InvalidDataException("Unmapped gravel terrain family: " + source)
    };
    internal NativeMaterial Convert(PssgShaderInstance source, Vector3 origin,
        Dictionary<string, string> textures, Func<string, string, string> resolve)
    {
        string sourceKind = source.GetShaderGroup().Id, target = Kind(sourceKind);
        var group = new XElement(groups[target]);
        var instance = new XElement(templates[target]);
        var original = new XElement("source"); source.GetShaderGroup().WriteXml(original); source.WriteXml(original);
        var sourceDefinitions = original.Element("SHADERGROUP")!.Elements("SHADERINPUTDEFINITION").ToArray();
        var sourceInputs = original.Element("SHADERINSTANCE")!.Elements("SHADERINPUT")
            .ToDictionary(i => (string)sourceDefinitions[(int)i.Attribute("parameterID")!].Attribute("name")!, StringComparer.Ordinal);
        var targetDefinitions = group.Elements("SHADERINPUTDEFINITION").ToArray();
        var defaults = instance.Elements("SHADERINPUT").ToDictionary(i => (int)i.Attribute("parameterID")!);
        instance.Elements("SHADERINPUT").Remove();
        var unused = new HashSet<string>(sourceInputs.Keys);
        for (int i = 0; i < targetDefinitions.Length; i++)
        {
            string name = (string)targetDefinitions[i].Attribute("name")!, type = (string)targetDefinitions[i].Attribute("type")!;
            var input = sourceInputs.GetValueOrDefault(name);
            // Engine-bound resources such as TShadowMask are deliberately
            // unsaved. Writing a black fallback here disables native shadows.
            if(input is null && !defaults.ContainsKey(i)) continue;
            if (type == "texture")
            {
                string key;
                if (input?.Attribute("texture") is {} reference)
                {
                    key = resolve(name, reference.Value); unused.Remove(name);
                }
                else if (name == "TDiffuseAlphaMap") key = textures["txDiffuse"];
                else if (name is "TDiffuseSpecMap2" or "TDiffuseSpecMap3")
                    key = textures.GetValueOrDefault("TDiffuseSpecMap1") ?? textures["txDiffuse"];
                else if (name.StartsWith("TNormal", StringComparison.Ordinal) || name == "TlargeNormalMap") key = "nord_normal_dxt5";
                else if (name is "TBlendMap2" or "TBlendMap3") key = "nord_black";
                else if (name == "TTreeSkyLightMapWS") key = resolve(name, "@ambient");
                else if (name == "TSourceSunMap") key = resolve(name, "@sun");
                else if (name is "TAmbientOcclusion" or "TOcclusionMap" or "TColourMap") key = "nord_white";
                else if (name.StartsWith("T", StringComparison.Ordinal)) key = "nord_black";
                else throw new InvalidDataException("Unknown texture input " + target + "/" + name);
                textures[name] = key;
                instance.Add(new XElement("SHADERINPUT", new XAttribute("parameterID", i), new XAttribute("type", "texture"), new XAttribute("texture", name)));
                continue;
            }
            var next = input is not null ? new XElement(input) : defaults.TryGetValue(i, out var value) ? new XElement(value) :
                throw new InvalidDataException("Missing constant " + target + "/" + name);
            unused.Remove(name);
            next.SetAttributeValue("parameterID", i);
            string format = (string)targetDefinitions[i].Attribute("format")!;
            if ((string?)next.Attribute("format") == "float4" && format == "float3") next.Value = string.Join(" ", Words(next.Value).Take(3));
            next.SetAttributeValue("format", format);
            if (name == "SourceIndirectIntensity") next.Value = Parameter("gi_indirectLightingIntensity");
            if (name == "SourceDirectIntensity") next.Value = Parameter("gi_directLightingIntensity");
            if (name == "TreeDiffuseScale") next.Value = Parameter("treeSheetDiffuse");
            if (name == "TreeShadowing") next.Value = Parameter("treeShadowing");
            if (name == "TreeDiffuseScale") next.Value = Values(Number("treeSheetDiffuse")*Number("gi_directLightingIntensity"));
            if (name == "SkyMapMapping") next.Value = Values(1/Number("ambientLightMapXScale"),1/Number("ambientLightMapZScale"),.5f+origin.X/Number("ambientLightMapXScale"),.5f+origin.Z/Number("ambientLightMapZScale"));
            if (name == "SunMapMapping") next.Value = Values(sun.ScaleX/sun.Resolution,sun.ScaleZ/sun.Resolution,(origin.X-sun.OriginX)*sun.ScaleX/sun.Resolution,(origin.Z-sun.OriginZ)*sun.ScaleZ/sun.Resolution);
            if (name == "TreeAmbientColour")
            {
                uint tint=unchecked((uint)int.Parse(Parameter("treeAmbientTintColour"),CultureInfo.InvariantCulture));
                float strength=Number("treeAmbientMultiplier")*Number("treeSheetAmbient")*Number("gi_indirectLightingIntensity");
                next.Value=Values(((tint>>16)&255)/255f*strength,((tint>>8)&255)/255f*strength,(tint&255)/255f*strength);
            }
            if (name == "ColourMapScale" && input is null) next.Value = "1";
            if (name == "TColourMapChannelScale" && input is null) next.Value = "1 1 1 1";
            if (name == "TColourMapChannelMin" && input is null) next.Value = "0 0 0 0";
            if(name is "TColourMapChannelScale" or "TColourMapChannelMin" && textures.TryGetValue("TColourMap",out string? colourMap) && colourRanges.TryGetValue(colourMap,out var range))
            {
                byte[] bytes=name=="TColourMapChannelScale"?range.Scale:range.Min;
                next.Value=Values(Enumerable.Range(0,4).Select(n=>BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(n*4))).ToArray());
            }
            // Source WSM layers sample original world XZ, even after the venue
            // is translated into the target coordinate frame.
            bool worldMap = sourceKind == "terrain_wsm_blend_nm.fx" && name is "Map1UVScaleAndOffset" or "Map2UVScaleAndOffset" ||
                sourceKind == "terrain_wsm_edge_nm.fx" && name == "Map2UVScaleAndOffset";
            if (worldMap)
            {
                var f = Words(next.Value).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                if (f.Length != 4 || f[0] == 0 || f[1] == 0) throw new InvalidDataException("Invalid WSM mapping");
                f[2] += origin.X / f[0]; f[3] += origin.Z / f[1];
                next.Value = string.Join(" ", f.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
            }
            instance.Add(next);
        }
        instance.SetAttributeValue("id", source.Id);
        instance.SetAttributeValue("shaderGroup", "#" + target);
        instance.SetAttributeValue("parameterCount", targetDefinitions.Length);
        instance.SetAttributeValue("parameterSavedCount", instance.Elements("SHADERINPUT").Count());
        mappings.Add(new { Source = source.Id, SourceGroup = sourceKind, Target = target, UnusedSourceInputs = unused.Order().ToArray() });
        bool cutout = target == "pikes_foliage.fx" || sourceKind.Contains("foliage", StringComparison.OrdinalIgnoreCase);
        return new(group, instance, layouts[target], !cutout);
    }
    string Parameter(string name) => PikesSourceProfile.Parameter(lighting, name);
    float Number(string name) => float.Parse(Parameter(name),CultureInfo.InvariantCulture);
    static string Values(params float[] values) => string.Join(" ",values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
    static string[] Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    internal void Write(string output) => Files.Json(Path.Combine(output, "approved-material-mappings.json"), mappings);
}
