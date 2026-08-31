using System.Buffers.Binary;

namespace AirMic.Core.Protocol;

public readonly record struct AudioPacketHeader(
    byte Flags,
    uint Sequence,
    ulong TimestampMicros,
    uint SampleRate,
    ushort SampleCount,
    byte Channels,
    byte Format,
    uint SessionId)
{
    public const int Size = 32;
    public const byte CurrentVersion = 1;
    public const byte Pcm16LittleEndian = 1;
    public const byte EncryptedFlag = 1;
    private static ReadOnlySpan<byte> Magic => "AMIC"u8;

    public bool IsEncrypted => (Flags & EncryptedFlag) != 0;
    public bool IsMuted => (Flags & 2) != 0;

    public void Write(Span<byte> destination)
    {
        if (destination.Length < Size) throw new ArgumentException("Header buffer is too small.", nameof(destination));
        Magic.CopyTo(destination);
        destination[4] = CurrentVersion;
        destination[5] = Flags;
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], Size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], Sequence);
        BinaryPrimitives.WriteUInt64BigEndian(destination[12..20], TimestampMicros);
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..24], SampleRate);
        BinaryPrimitives.WriteUInt16BigEndian(destination[24..26], SampleCount);
        destination[26] = Channels;
        destination[27] = Format;
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], SessionId);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out AudioPacketHeader header)
    {
        header = default;
        if (source.Length < Size || !source[..4].SequenceEqual(Magic)) return false;
        if (source[4] != CurrentVersion || BinaryPrimitives.ReadUInt16BigEndian(source[6..8]) != Size) return false;

        var rate = BinaryPrimitives.ReadUInt32BigEndian(source[20..24]);
        var samples = BinaryPrimitives.ReadUInt16BigEndian(source[24..26]);
        var channels = source[26];
        var format = source[27];
        if (rate is not (16000 or 24000 or 48000) || samples == 0 || channels != 1 || format != Pcm16LittleEndian)
            return false;

        header = new AudioPacketHeader(
            source[5],
            BinaryPrimitives.ReadUInt32BigEndian(source[8..12]),
            BinaryPrimitives.ReadUInt64BigEndian(source[12..20]),
            rate,
            samples,
            channels,
            format,
            BinaryPrimitives.ReadUInt32BigEndian(source[28..32]));
        return true;
    }
}
