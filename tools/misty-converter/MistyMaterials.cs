using DiRT2VR.Nordschleife;

internal sealed class MistyMaterials(Scene scene,string game)
{
    readonly MistyDecals decals=new(scene,game);
    readonly MistyNativeMaterials native=new(scene,game);
    readonly Dictionary<string,Material> converted=new(StringComparer.Ordinal);
    internal readonly List<object> Report=[];
    internal static float Opacity(Material material)
    {
        float alpha=material.Properties.TryGetValue("alpha",out var value)?value[0]:1;
        if(!float.IsFinite(alpha)||alpha is <0 or >1)throw new InvalidDataException("Invalid source decal opacity: "+material.Id);
        return alpha;
    }
    internal Material Convert(Material source)
    {
        if(converted.TryGetValue(source.Id,out var found))return found;
        var result=source;
        string note="Source diffuse/UV/alpha with existing Nordschleife static shader approximation; AC normal/specular packing omitted.";
        if(source.Shader=="ksPerPixelAT_NM")
        {
            if(!source.AlphaTest||source.Blend!=0)throw new InvalidDataException("Unexpected alpha-test material: "+source.Id);
            result=source with {Shader="ksPerPixelAT"};
            note="Static alpha test retained; source normal map omitted in desktop prototype.";
        }
        if(source.Name is "road_asphalt" or "grass_terrain_new1a")
        {
            if(source.Shader!="ksMultilayer_fresnel_nm"||source.Blend!=0||source.AlphaTest)throw new InvalidDataException("Unexpected multilayer profile: "+source.Id);
            result=native.Detail(source);
            note="Source RGBA mask sum, all four original detail textures/scales, magicMult and broad RGB modulation. Source detail normal uses its vector tiling and a derivative UV frame. Native shadow/SSAO/fog; explicitly dry with no specular term. Exact AC/CSP lighting remains approximate.";
        }
        if(source.Name=="lake1")
            note="Original lake geometry is exported separately to the native non-interactive water manager, with source normal texture and native timed waves/reflections. CSP caustics and exact source shading remain unsupported.";
        if(source.Blend==0&&source.Name!="lake1"&&source.Textures.ContainsKey("txNormal")&&source.Shader is "ksPerPixelNM" or "ksPerPixelMultiMap" or "ksPerPixelAT_NM" or "ksPerPixelMultiMap_AT" or "ksPerPixelNM_UVMult")
        {
            result=native.Normal(source);
            note="Source tangent normal repacked RGB to native DXT5nm AG, all authored mip levels retained. Original diffuse and cutout coverage; specular map semantics remain pending.";
        }
        if(source.Name is "waterfall" or "waterfall2" or "beachwaves")
        {
            result=native.Flow(source);
            note="Native timed UV scrolling with soft source-alpha blending in the lab's scrolling shader; mirrored V/texture preserves source flow direction and CSP diffuse speed. No normal flow or caustics.";
        }
        if(source.Id.StartsWith("rt_misty_loch_ships.kn5:",StringComparison.Ordinal))
        {
            result=native.Boat(source);
            note="Native 10 cm rigid floating motion shared by hull, chrome and sail. Authored ten-minute sailing paths are not reproduced.";
        }
        if(source.Name is "b3road_patches" or "skidmarks1" or "bridge_dirt" or "helipad" or "tyremarks3" or "tyremarks2" or "groove")
        {
            if(source.Blend!=1)throw new InvalidDataException("Road decal coverage changed: "+source.Id);
            result=decals.Convert(source);
            note="Native terrain DECAL pass; original diffuse alpha/UVs and source material opacity retained in vertex alpha, neutral occlusion/normal, specular and Fresnel disabled.";
        }
        _=Visuals.Target(result);
        Report.Add(new {source.Id,source.Name,SourceShader=source.Shader,AdapterShader=result.Shader,Target=Visuals.Target(result),SourceTextures=source.Textures,AdaptedTextures=result.Textures,Approximation=note});
        converted.Add(source.Id,result);return result;
    }
}
