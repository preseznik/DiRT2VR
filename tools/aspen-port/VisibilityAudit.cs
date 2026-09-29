using System.Buffers.Binary;

internal static class VisibilityAudit
{
    internal static object AllVisible(byte[] bytes)
    {
        _ = Check(bytes);
        var before = bytes.ToArray();
        int I(int p) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p, 4));
        int maskStart = I(24), maskBytes = I(16), start = I(20), leaves = 0;
        if ((long)maskStart + maskBytes > I(28) || maskStart > 0xffffff) throw new InvalidDataException("No space for raw diagnostic mask.");
        var owned = new HashSet<int>(Enumerable.Range(maskStart, maskBytes));
        for (int i = 0; i < I(4); i++)
        {
            int p = start + i * 6;
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p, 2)) != 0) continue;
            leaves++;
            bytes[p+2]=(byte)maskStart; bytes[p+3]=(byte)(maskStart>>8); bytes[p+4]=(byte)(maskStart>>16); bytes[p+5]=0;
            foreach (int offset in Enumerable.Range(p+2,4)) owned.Add(offset);
        }
        bytes.AsSpan(maskStart,maskBytes).Fill(255);
        _ = Check(bytes);
        if (Enumerable.Range(0,bytes.Length).Any(i => bytes[i] != before[i] && !owned.Contains(i))) throw new InvalidDataException("Unexpected visibility patch extent.");
        return new { Leaves=leaves, MaskBytes=maskBytes, BoundsAndIdentitiesPreserved=true, DiagnosticOnly=true };
    }
    internal static object Check(byte[] bytes)
    {
        int I(int p) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p, 4));
        if (bytes.Length < 128 || I(0) != 4) throw new InvalidDataException("Unsupported visibility version.");
        int count = I(4), expectedLeaves = I(8), maskBytes = I(16), start = I(20), maskStart = I(24), maskEnd = I(28);
        if (start != 128 || count <= 0 || (long)start + (long)count * 6 > maskStart || maskBytes <= 0 || maskEnd > bytes.Length || maskStart >= maskEnd)
            throw new InvalidDataException("Invalid visibility section boundaries.");
        int leaves = 0;
        for (int i = 0; i < count; i++)
        {
            int p = start + i * 6;
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p, 2)) != 0) continue;
            leaves++;
            int offset = bytes[p+2] | bytes[p+3] << 8 | bytes[p+4] << 16;
            int codec = bytes[p+5];
            if (offset < maskStart || offset >= maskEnd) throw new InvalidDataException("Visibility mask outside its section.");
            if (codec == 0) { if ((long)offset + maskBytes > maskEnd) throw new InvalidDataException("Truncated raw mask."); continue; }
            if (codec != 1) throw new InvalidDataException("Unknown visibility codec.");
            int written = 0;
            while (true)
            {
                if (offset >= maskEnd) throw new InvalidDataException("Unterminated mask.");
                int control = bytes[offset++];
                if (control == 0) break;
                int run = control & 127;
                offset += control >= 128 ? 1 : run;
                written += run;
                if (written > maskBytes || offset > maskEnd) throw new InvalidDataException("Visibility mask run exceeds bounds.");
            }
            if (written != maskBytes) throw new InvalidDataException("Short visibility mask.");
        }
        if (leaves != expectedLeaves) throw new InvalidDataException("Visibility leaf count mismatch.");
        return new { Version = 4, Nodes = count, Leaves = leaves, MaskBytes = maskBytes, BytesPreserved = true, RuntimeValidated = false };
    }
}
