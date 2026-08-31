using System.Buffers.Binary;
using System.Security.Cryptography;
using AirMic.Core.Protocol;

namespace AirMic.Core.Security;

public sealed class AudioPacketCipher : IDisposable
{
    public const int TagSize = 16;
    private readonly AesGcm _aes;

    public AudioPacketCipher(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32) throw new ArgumentException("AirMic requires a 256-bit session key.", nameof(key));
        _aes = new AesGcm(key, TagSize);
    }

    public byte[] Encrypt(AudioPacketHeader header, ReadOnlySpan<byte> pcm)
    {
        var datagram = new byte[AudioPacketHeader.Size + pcm.Length + TagSize];
        header.Write(datagram);
        var nonce = BuildNonce(header.SessionId, header.Sequence);
        _aes.Encrypt(nonce, pcm, datagram.AsSpan(AudioPacketHeader.Size, pcm.Length),
            datagram.AsSpan(AudioPacketHeader.Size + pcm.Length, TagSize), datagram.AsSpan(0, AudioPacketHeader.Size));
        return datagram;
    }

    public bool TryDecrypt(ReadOnlySpan<byte> datagram, uint expectedSessionId, out AudioPacketHeader header, out byte[] pcm)
    {
        header = default;
        pcm = [];
        if (datagram.Length < AudioPacketHeader.Size + TagSize || !AudioPacketHeader.TryParse(datagram, out header)) return false;
        if (!header.IsEncrypted || header.SessionId != expectedSessionId) return false;
        var cipherLength = datagram.Length - AudioPacketHeader.Size - TagSize;
        if (cipherLength != header.SampleCount * header.Channels * 2) return false;

        pcm = new byte[cipherLength];
        try
        {
            _aes.Decrypt(BuildNonce(header.SessionId, header.Sequence),
                datagram.Slice(AudioPacketHeader.Size, cipherLength),
                datagram.Slice(AudioPacketHeader.Size + cipherLength, TagSize), pcm,
                datagram[..AudioPacketHeader.Size]);
            return true;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(pcm);
            pcm = [];
            return false;
        }
    }

    private static byte[] BuildNonce(uint sessionId, uint sequence)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(0, 4), sessionId);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4, 8), sequence);
        return nonce;
    }

    public void Dispose() => _aes.Dispose();
}
