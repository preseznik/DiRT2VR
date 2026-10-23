using System.Text.Json;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static partial class PikesRoadObjects
{
    static void UniqueSignResources(PssgFile file)
    {
        var duplicates=file.Elements<PssgElement>()
            .SelectMany(e=>e.Attributes.Where(a=>a.Name=="id").Select(a=>a.DisplayValue))
            .GroupBy(id=>id,StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>g.Key).ToArray();
        if(duplicates.Length!=0)
            throw new InvalidDataException($"Duplicate object resource IDs ({duplicates.Length}): {string.Join(", ",duplicates.Take(3))}");
    }
}
