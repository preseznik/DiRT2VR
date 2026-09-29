using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

// Layouts: pinned EGO Engine Modding 010 Templates/trees.bt and ornaments.bt.
internal static class Placements
{
    internal static byte[] Convert(byte[] source, bool trees, List<object> report, XDocument? entities = null)
    {
        void Range(int offset, int size)
        {
            if (offset < 0 || size < 0 || (long)offset + size > source.Length)
                throw new InvalidDataException($"Placement range outside source: offset {offset}, size {size}, length {source.Length}.");
        }
        int I(int offset) { Range(offset, 4); return BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, 4)); }
        string S(int offset)
        {
            if (offset == -1) return "";
            Range(offset, 1); int end = Array.IndexOf(source, (byte)0, offset);
            if (end < 0 || end - offset > 1024) throw new InvalidDataException("Invalid placement string.");
            return Encoding.ASCII.GetString(source, offset, end - offset);
        }
        if (I(0) != 0 || I(8) != 1) throw new InvalidDataException("Unsupported placement version/list count.");
        int list = I(4), refs = I(list + 36), refCount = I(list + 40), instances = I(list + 44), instanceCount = I(list + 48);
        int sourceRefSize = trees ? 40 : 48, sourceInstanceSize = trees ? 76 : 88;
        if (refCount < 1 || refCount > 65536 || instanceCount < 0 || instanceCount > 1000000)
            throw new InvalidDataException("Invalid placement counts.");
        Range(refs, checked(refCount * sourceRefSize)); Range(instances, checked(instanceCount * sourceInstanceSize));
        int animations = 0;
        if (!trees)
        {
            if (I(16) != 1 || I(24) != 1) throw new InvalidDataException("Unsupported placement auxiliary lists.");
            int dependent = I(12);
            if (I(dependent) != 0 || I(dependent + 4) != 0 || I(dependent + 12) != 0 || I(dependent + 20) != 0)
                throw new InvalidDataException("Dependent prop conversion is not implemented.");
            animations = I(I(20) + 8);
        }
        var groups = new Dictionary<int, List<int>>();
        for (int r = 0; r < refCount; r++)
            if (!groups.TryAdd(I(refs + r * sourceRefSize + 4), new List<int>())) throw new InvalidDataException("Duplicate placement reference ID.");
        var ids = new HashSet<int>();
        for (int n = 0; n < instanceCount; n++)
        {
            int p = instances + n * sourceInstanceSize;
            if (!groups.TryGetValue(I(p), out var group) || !ids.Add(I(p + 4))) throw new InvalidDataException("Unknown reference or duplicate placement ID.");
            if (!trees && (I(p + 68) != 0 || I(p + 84) != -1)) throw new InvalidDataException("Unsupported prop shadow/mode layer.");
            group.Add(p);
        }
        var dynamicInstances = entities?.Descendants("TEMPLATEENTITYINSTANCE").ToArray() ?? [];
        var dynamicGroups = dynamicInstances.ToLookup(e => ((string?)e.Attribute("uri") ?? "").TrimStart('#'), StringComparer.Ordinal);
        foreach (var entity in dynamicInstances)
            if (((string?)entity.Attribute("uri"))?.StartsWith('#') != true ||
                !int.TryParse((string?)entity.Attribute("instanceID"), out int id) || id < 0 || !ids.Add(id))
                throw new InvalidDataException("Invalid or overlapping dynamic placement ID.");
        if (!trees && entities is not null && (ids.Count != I(list + 28) || ids.Any(id => id < 0 || id >= ids.Count)))
            throw new InvalidDataException("Static and dynamic placements do not cover the source slot space.");
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        int header = trees ? 40 : 44, targetRefSize = trees ? 48 : 56;
        writer.Write(new byte[header + refCount * targetRefSize]);
        void Put(int at, int value) { long saved = output.Position; output.Position = at; writer.Write(value); output.Position = saved; }
        void Copy(int from, int at, int size) { Range(from, size); long saved = output.Position; output.Position = at; writer.Write(source, from, size); output.Position = saved; }
        int String(string value) { int at = checked((int)output.Position); writer.Write(Encoding.ASCII.GetBytes(value)); writer.Write((byte)0); return at; }
        int root = trees ? 0 : 4;
        Copy(list, root, 24); Put(root + 28, I(list + 32)); Put(root + 32, header); Put(root + 36, refCount);
        var mapping = new List<object>(); int slot = 0, mappedDynamic = 0;
        for (int r = 0; r < refCount; r++)
        {
            int p = refs + r * sourceRefSize, q = header + r * targetRefSize, id = I(p + 4);
            string name = S(I(p)); var group = groups[id];
            if (name.Length == 0) throw new InvalidDataException("Missing placement reference name.");
            int max = I(p + (trees ? 36 : 40));
            if (max < group.Count || max > 1000000) throw new InvalidDataException("Invalid placement capacity.");
            var dynamicGroup = dynamicGroups[name].OrderBy(e => (int)e.Attribute("instanceID")!).ToArray();
            if (!trees && entities is not null && max != group.Count + dynamicGroup.Length)
                throw new InvalidDataException("Placement capacity does not match static and dynamic instances: " + name);
            Put(q, String(name)); Copy(p + 8, q + 4, 24);
            if (!trees) { Copy(p + 32, q + 28, 8); Put(q + 52, String(S(I(p + 44)))); }
            while (output.Position % 4 != 0) writer.Write((byte)0);
            int data = checked((int)output.Position), fields = trees ? 28 : 36;
            Put(q + fields, max); Put(q + fields + 4, slot); Put(q + fields + 8, data); Put(q + fields + 12, group.Count);
            if (trees) Put(q + 44, I(p + 32));
            for (int n = 0; n < group.Count; n++)
            {
                int instance = group[n];
                // Preserve authored transform, colour, mechanics flag and landmark exactly.
                writer.Write(source, instance + 8, trees ? 64 : 60);
                if (!trees) writer.Write(I(instance + 72));
                mapping.Add(new { Name = name, SourceId = I(instance + 4), TargetId = slot + n, Dynamic = false });
            }
            for (int n = 0; n < dynamicGroup.Length; n++)
            {
                var entity = dynamicGroup[n]; int sourceId = (int)entity.Attribute("instanceID")!, targetId = slot + group.Count + n;
                entity.SetAttributeValue("instanceID", targetId);
                mapping.Add(new { Name = name, SourceId = sourceId, TargetId = targetId, Dynamic = true });
                mappedDynamic++;
            }
            slot = checked(slot + max);
        }
        if (slot != I(list + 28)) throw new InvalidDataException("Placement capacity total changed.");
        if (mappedDynamic != dynamicInstances.Length) throw new InvalidDataException("Unresolved dynamic placement reference.");
        Put(root + 24, slot);
        var bytes = output.ToArray();
        // Independently read back each placement payload by its saved offsets.
        int Read(int p) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p, 4));
        for (int r = 0; r < refCount; r++)
        {
            int p = refs + r * sourceRefSize, q = header + r * targetRefSize;
            var group = groups[I(p + 4)]; int data = Read(q + (trees ? 36 : 44));
            if (Read(q + (trees ? 40 : 48)) != group.Count) throw new InvalidDataException("Placement readback count changed.");
            for (int n = 0; n < group.Count; n++)
                if (!bytes.AsSpan(data + n * 64, trees ? 64 : 60).SequenceEqual(source.AsSpan(group[n] + 8, trees ? 64 : 60)) ||
                    (!trees && Read(data + n * 64 + 60) != I(group[n] + 72)))
                    throw new InvalidDataException("Placement transform/flags changed.");
        }
        report.Add(new { File = trees ? "trees.bin" : "ornaments.bin", References = refCount, Instances = instanceCount,
            DynamicInstances = mappedDynamic, TransformsPreserved = true, UnconvertedPathAnimations = animations, Mapping = mapping, RuntimeValidated = false });
        return bytes;
    }
}
