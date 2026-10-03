internal static class SmelterMaterials
{
    // County Loop's dry-day candidate. These mappings require runtime acceptance,
    // particularly shore foam and rain-only meshes; they do not affect Aspen.
    internal static readonly IReadOnlyDictionary<string,string> Aliases = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        ["ground_clutter.fx"]="object_simple_mesh.fx",
        ["Object_RainDrops.fx"]="object_simple_blended.fx",
        ["Object_Runoff.fx"]="object_simple_reflective.fx",
        ["Object_AnimEmis.fx"]="object_simple_emissive.fx",
        ["Object_Light_Shafts.fx"]="volumetrics.fx",
        ["Object_Light_Shafts_TwoSided.fx"]="volumetrics.fx",
        ["interactive_water_shore.fx"]="interactive_water.fx",
        ["water_shore.fx"]="water.fx",
        ["terrain_infield_overlay_feather_nm.fx"]="terrain_infield_nm.fx"
    };
}
