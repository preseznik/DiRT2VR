using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Same source layout, separate authored weather. Reuse the verified geometry,
// replacing every weather-dependent map and shader constant by source identity.
internal static class PikesGravelConditions
{
    internal static bool GameFile(string name)=>new[]{".xml",".bin",".pssg",".ens",".jpk",".vis",".clm",".grs",".cqtc",".cns",".txt",".htf",".nfs"}.Contains(Path.GetExtension(name)) && !name.StartsWith("source-",StringComparison.Ordinal);
}
