internal static class SmelterCrowds
{
    // Keep every placement, using stock characters for source-only identities.
    // Staff animation restoration is a later milestone, as it was for Aspen.
    internal static readonly IReadOnlyDictionary<string,string> Aliases = new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["f_pdc_cr_01"]="f_cr", ["f_pdc_cr_02"]="f_cr",
        ["m_pdc_cr_01"]="m_cr", ["m_pdc_cr_02"]="m_cr",
        ["m_static_var_D"]="m_cr", ["m_anim_static_01"]="m_cr",
        ["f_anim_static_01"]="f_cr", ["pre-race_photographer_02"]="m_cr",
        ["pre-race_marshal_01"]="ma_np", ["pre-race_marshal_02"]="ma_np",
        ["pre-race_marshal_03"]="ma_np", ["ma_fl"]="ma_np"
    };
}
