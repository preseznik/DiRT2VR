#pragma once
namespace vr {
struct PikesLayout { const char* country; const char* folder; unsigned chunks; const char* hash; };
inline constexpr PikesLayout pikesLayouts[]{
    {"usa","d2ve_pikes_tarmac",3975,"5AEC9DFCEC8A36077951A6B8A738D6F17FB5A933B2DBB2B523D03D4626D950D4"},
    {"usa","d2ve_pikes_gday",4010,"35BD2D2CE6DB6EE7D8D41B0B01BA23E5052A70C3C0C145CB684DCB14A9B6F3B3"},
    {"usa","d2ve_pikes_galt",4010,"35BD2D2CE6DB6EE7D8D41B0B01BA23E5052A70C3C0C145CB684DCB14A9B6F3B3"},
    {"usa","d2ve_pikes_gwet",4010,"35BD2D2CE6DB6EE7D8D41B0B01BA23E5052A70C3C0C145CB684DCB14A9B6F3B3"}
};
}
