using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

// A bounded patch for the inspected Battersea route_1 version-4 visibility file.
// This is not a general PVS compiler. Preserve object IDs; disable donor occlusion.
internal static class TrackVisibility
{
    internal static void Write(string source, string output, Vector3 min, Vector3 max)
    {
        var bytes = File.ReadAllBytes(source);
        var original = bytes.ToArray();
        int I(int p) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p, 4));
        Vector3 V(int p) => new(BitConverter.ToSingle(bytes, p), BitConverter.ToSingle(bytes, p+4), BitConverter.ToSingle(bytes, p+8));
        void Set(int p, Vector3 v)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p,4),v.X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p+4,4),v.Y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p+8,4),v.Z);
        }
        if (Convert.ToHexString(SHA256.HashData(bytes)) != "B5A4CE10FDD433DF71814EBF081581ED6CED3966E34E01DB645809A7F1DEAA97" ||
            bytes.Length != 151008 || I(0) != 4 || I(12) != 599 || I(64) != 25)
            throw new InvalidDataException("Unsupported donor visibility layout.");
        var terrain = new HashSet<int>();
        int start = I(28), cursor = start, target = -1, targetNode = -1;
        // Each spatial node is 48 bytes followed by count 32-byte bounds records.
        // The last node has a zero next pointer. Type 0 records index terrain tiles.
        for (int index = 0; index < I(12); index++)
        {
            if (cursor < start || cursor + 48 > bytes.Length || (I(cursor+28)&65535) != index)
                throw new InvalidDataException("Visibility node sequence mismatch.");
            int count = (int)((uint)I(cursor+28)>>16);
            int end = checked(cursor+48+count*32);
            if (end > bytes.Length || I(cursor+32) != (index == I(12)-1 ? 0 : end))
                throw new InvalidDataException("Visibility record span mismatch.");
            for (int r=cursor+48; r<end; r+=32)
            {
                if (I(r+12) != 0) continue;
                int id=I(r+28);
                if (!terrain.Add(id)) throw new InvalidDataException("Duplicate terrain visibility ID.");
                if (id == 8) { target=r; targetNode=cursor; }
            }
            cursor=end;
        }
        if (cursor != bytes.Length || terrain.Count != 25 || targetNode != start ||
            Vector3.Distance(V(target),new(-30.440174f,-2.0853493f,-76.42272f)) > .01f ||
            Vector3.Distance(V(target+16),new(247.45364f,4.777686f,158.93959f)) > .01f)
            throw new InvalidDataException("Visibility tile 8 is not the expected HIGH/LOW_4_4 donor union.");
        var beforeMin=V(target); var beforeMax=V(target+16);
        var afterMin=Vector3.Min(beforeMin,min); var afterMax=Vector3.Max(beforeMax,max);
        Set(target,afterMin); Set(target+16,afterMax);
        // This tile is directly in the scene root, whose bounds already enclose it.
        if (Vector3.Min(V(start),afterMin)!=V(start) || Vector3.Max(V(start+16),afterMax)!=V(start+16))
            throw new InvalidDataException("Prototype exceeds the visibility root bounds.");
        var masks=VisibilityMasks.MakeAllVisible(bytes);
        var changed=Enumerable.Range(0,bytes.Length).Where(i=>bytes[i]!=original[i]).ToArray();
        if (changed.Any(i=>!(i>=target && i<target+12 || i>=target+16 && i<target+28 || masks.Owns(i))))
            throw new InvalidDataException("Unexpected visibility patch extent.");
        File.WriteAllBytes(output,bytes);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!,"visibility.json"),JsonSerializer.Serialize(new {
            Tile=8, NodeCount=I(12), TerrainRecords=terrain.Count, RecordOffset=target,
            BeforeMin=new[]{beforeMin.X,beforeMin.Y,beforeMin.Z}, BeforeMax=new[]{beforeMax.X,beforeMax.Y,beforeMax.Z},
            AfterMin=new[]{afterMin.X,afterMin.Y,afterMin.Z}, AfterMax=new[]{afterMax.X,afterMax.Y,afterMax.Z},
            ChangedByteCount=changed.Length, RegionMasksUnchanged=false, AllVisibleLeaves=masks.Leaves.Length,
            masks.MaskOffset, masks.MaskBytes, NormalFrustumCullingRetained=true, RuntimeValidated=false,
            SourceSha256=Convert.ToHexString(SHA256.HashData(original))
        },new JsonSerializerOptions {WriteIndented=true}));
    }
}
