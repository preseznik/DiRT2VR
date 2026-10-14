using System.Security.Cryptography;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class SpaFenceRepair
{
    internal static bool IsCoverageTexture(string reference) => reference.Split('#').Last() is
        "fence_05_d.tga" or "chain_fence_cheap_d.tga" or "fence_03_d.tga" or "grandstand_fence_01_d.tga";
}
