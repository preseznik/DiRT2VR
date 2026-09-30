using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

// Supports only the unprotected 0x26f container observed with
// the offline provider. This does not construct or validate career progression.
public sealed class Dirt2SaveContainer
{
    public const int MaxBytes = 262144;
    private const int HeaderBytes = 28;
    private const int PlainChunk = 1024;
    private const int StoredChunk = 1028;
    private static readonly byte[] Key = Convert.FromHexString(
        "BF238E52208261B11FB50901E78E45AC4660153565F09295305484E1F05166EC");
    private readonly byte[] header;
    private readonly byte[] payload;
    private Dirt2SaveContainer(byte[] header, byte[] payload)
    { this.header = header; this.payload = payload; }

    public byte[] Payload => (byte[])payload.Clone();

    // Factory for a NEW offline record. Reserved header bytes and
    // unused block tails start at zero; no heap padding is inherited from a save.
    public static byte[] CreateRecord(byte[] logicalPayload, uint profileIdentity)
    {
        if (logicalPayload == null || logicalPayload.Length == 0 ||
            logicalPayload.Length > (MaxBytes - HeaderBytes) / StoredChunk * PlainChunk)
            throw new InvalidDataException("Invalid new record length.");
        byte[] cleanHeader = new byte[HeaderBytes];
        Write(cleanHeader, 0, 24);
        Write(cleanHeader, 8, 0x26f);
        Write(cleanHeader, 20, profileIdentity);
        byte[] cleanPayload = new byte[(logicalPayload.Length + PlainChunk - 1) / PlainChunk * PlainChunk];
        logicalPayload.CopyTo(cleanPayload, 0);
        return new Dirt2SaveContainer(cleanHeader, cleanPayload).Encode();
    }
    private static uint Read(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    public static uint Checksum(byte[] bytes, int offset)
    {
        uint sum = 0;
        for (int i = offset; i <= bytes.Length - 4; i += 4)
            sum = unchecked(sum + Read(bytes, i));
        return sum;
    }

    public static Dirt2SaveContainer Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < HeaderBytes + StoredChunk ||
            bytes.Length > MaxBytes || (bytes.Length - HeaderBytes) % StoredChunk != 0)
            throw new InvalidDataException("Invalid save container length.");
        if (Read(bytes, 0) != 24 || Read(bytes, 8) != 0x26f)
            throw new InvalidDataException("Unsupported protected header or save version.");
        if (Read(bytes, 12) != bytes.Length - HeaderBytes)
            throw new InvalidDataException("Payload length differs from header.");
        if (Read(bytes, 4) != Checksum(bytes, HeaderBytes))
            throw new InvalidDataException("Save payload checksum mismatch.");
        int chunks = (bytes.Length - HeaderBytes) / StoredChunk;
        byte[] plain = new byte[chunks * PlainChunk];
        using (Aes aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = Key;
            using (ICryptoTransform decrypt = aes.CreateDecryptor())
                for (int i = 0; i < chunks; i++)
                {
                    int offset = HeaderBytes + i * StoredChunk;
                    if (Read(bytes, offset) != PlainChunk)
                        throw new InvalidDataException("Unsupported save chunk length.");
                    decrypt.TransformBlock(bytes, offset + 4, PlainChunk, plain, i * PlainChunk);
                }
        }
        return new Dirt2SaveContainer(bytes.AsSpan(0, HeaderBytes).ToArray(), plain);
    }

    // Retains opaque header identity fields and all padding/unknown payload bytes.
    // Deliberately returns bytes only: no API to overwrite any career on disk.
    public byte[] Encode(byte[]? replacement = null)
    {
        byte[] plain = replacement ?? payload;
        if (plain.Length == 0 || plain.Length % PlainChunk != 0 ||
            (long)plain.Length / PlainChunk * StoredChunk + HeaderBytes > MaxBytes)
            throw new InvalidDataException("Invalid decoded payload length.");
        int chunks = plain.Length / PlainChunk;
        byte[] bytes = new byte[HeaderBytes + chunks * StoredChunk];
        header.CopyTo(bytes, 0);
        Write(bytes, 12, (uint)(bytes.Length - HeaderBytes));
        using (Aes aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = Key;
            using (ICryptoTransform encrypt = aes.CreateEncryptor())
                for (int i = 0; i < chunks; i++)
                {
                    int offset = HeaderBytes + i * StoredChunk;
                    Write(bytes, offset, PlainChunk);
                    encrypt.TransformBlock(plain, i * PlainChunk, PlainChunk, bytes, offset + 4);
                }
        }
        Write(bytes, 4, Checksum(bytes, HeaderBytes));
        return bytes;
    }

    public XDocument ReadProfileXml()
    {
        uint count = Read(payload, 0);
        if (count < 1 || count > payload.Length - 4)
            throw new InvalidDataException("Invalid profile XML length.");
        int size = (int)count;
        if (payload[4 + size - 1] == 0) size--;
        string text = new UTF8Encoding(false, true).GetString(payload, 4, size);
        var settings = new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxBytes
        };
        using (var reader = XmlReader.Create(new StringReader(text), settings))
        {
            XDocument doc = XDocument.Load(reader);
            if (doc.Root == null || doc.Root.Name != "PlayerProfile")
                throw new InvalidDataException("Unexpected profile XML root.");
            return doc;
        }
    }
}
