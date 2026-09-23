using System.Buffers.Binary;

// DiRT 2 decoder RVAs 0x827D3A..0x827D65 and 0x7EAF20..0x7EAFE6:
// a view-tree leaf stores a 24-bit file offset and one-byte codec. Codec 0
// copies maskBytes verbatim; codec 1 is zero-terminated literal/repeat RLE.
internal static class VisibilityMasks
{
    internal record Patch(int[] Leaves, int MaskOffset, int MaskBytes)
    {
        internal bool Owns(int offset) => offset >= MaskOffset && offset < MaskOffset + MaskBytes ||
            Leaves.Any(p => offset >= p + 2 && offset < p + 6);
    }

    static int I(byte[] bytes, int p) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p, 4));

    internal static byte[] Decode(byte[] bytes, int offset, int codec, int length, int end)
    {
        if (offset < 0 || end > bytes.Length || offset >= end || length <= 0)
            throw new InvalidDataException("Invalid visibility mask range.");
        if (codec == 0)
        {
            if (offset > end - length) throw new InvalidDataException("Truncated raw visibility mask.");
            return bytes.AsSpan(offset, length).ToArray();
        }
        if (codec != 1) throw new InvalidDataException("Unknown visibility mask codec.");
        var output = new byte[length];
        int written = 0;
        while (offset < end)
        {
            int control = bytes[offset++];
            if (control == 0)
            {
                if (written != length) throw new InvalidDataException("Short decoded visibility mask.");
                return output;
            }
            int count = control & 127;
            int inputCount = control >= 128 ? 1 : count;
            if (offset > end - inputCount || written > length - count)
                throw new InvalidDataException("Visibility mask run exceeds its buffer.");
            if (control >= 128) output.AsSpan(written, count).Fill(bytes[offset]);
            else bytes.AsSpan(offset, count).CopyTo(output.AsSpan(written));
            written += count;
            offset += inputCount;
        }
        throw new InvalidDataException("Unterminated visibility mask.");
    }

    internal static Patch MakeAllVisible(byte[] bytes)
    {
        int count = I(bytes, 4), expectedLeaves = I(bytes, 8), start = I(bytes, 20);
        int maskStart = I(bytes, 24), maskEnd = I(bytes, 28), maskBytes = I(bytes, 16);
        if (count != 481 || expectedLeaves != 241 || start != 128 || maskStart != 3024 || maskBytes != 320 ||
            start + count * 6 > maskStart || maskEnd > bytes.Length || maskStart + maskBytes > maskEnd)
            throw new InvalidDataException("Unsupported view-tree/mask layout.");
        var leaves = Enumerable.Range(0, count).Select(i => start + i * 6)
            .Where(p => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p, 2)) == 0).ToArray();
        if (leaves.Length != expectedLeaves) throw new InvalidDataException("Visibility leaf count mismatch.");
        int Offset(int p) => bytes[p+2] | bytes[p+3]<<8 | bytes[p+4]<<16;
        // Decode every original mask before changing it. Reject malformed offsets or codecs.
        foreach (int p in leaves)
        {
            if (Offset(p) < maskStart) throw new InvalidDataException("Mask points outside its section.");
            _ = Decode(bytes, Offset(p), bytes[p+5], maskBytes, maskEnd);
        }
        // All leaves share a raw all-visible mask in the existing mask section.
        // No file growth, scene pointer relocation, object ID changes or executable patch.
        foreach (int p in leaves)
        {
            bytes[p+2] = (byte)maskStart;
            bytes[p+3] = (byte)(maskStart>>8);
            bytes[p+4] = (byte)(maskStart>>16);
            bytes[p+5] = 0;
        }
        bytes.AsSpan(maskStart, maskBytes).Fill(255);
        foreach (int p in leaves)
            if (Decode(bytes, Offset(p), bytes[p+5], maskBytes, maskEnd).Any(b => b != 255))
                throw new InvalidDataException("All-visible mask verification failed.");
        return new Patch(leaves, maskStart, maskBytes);
    }

    internal static void Test(string source)
    {
        int checks = 0;
        void Check(bool result) { if (!result) throw new InvalidDataException("Mask regression failed."); checks++; }
        void Reject(Action action)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new InvalidDataException("Malformed mask was accepted.");
        }
        Check(Decode([2, 0x12, 0x34, 0x83, 0xAB, 0], 0, 1, 5, 6).SequenceEqual(new byte[]{0x12,0x34,0xAB,0xAB,0xAB}));
        Check(Decode([0, 255, 0x10], 0, 0, 3, 3).SequenceEqual(new byte[]{0,255,0x10}));
        Reject(()=>Decode([1, 5],0,1,1,2));
        Reject(()=>Decode([0],0,1,3,1));
        Reject(()=>Decode([0x84, 1, 0],0,1,3,3));
        Reject(()=>Decode([1],0,0,3,1));
        Reject(()=>Decode([1],0,2,1,1));
        var before=File.ReadAllBytes(source); var after=before.ToArray();
        var patch=MakeAllVisible(after);
        Check(patch.Leaves.Length==241 && after.Length==before.Length);
        Check(Enumerable.Range(0,before.Length).All(i=>before[i]==after[i] || patch.Owns(i)));
        var broken=before.ToArray(); broken[patch.Leaves[0]+4]=255;
        Reject(()=>MakeAllVisible(broken));
        Console.WriteLine($"All {checks} visibility mask checks passed; 241 donor masks decoded and 241 replacement masks verified.");
    }
}
