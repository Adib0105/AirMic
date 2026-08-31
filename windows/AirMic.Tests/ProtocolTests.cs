using AirMic.Core.Protocol;
using AirMic.Core.Security;

namespace AirMic.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void HeaderRoundTrips()
    {
        var expected = new AudioPacketHeader(1, 42, 1_725_100_000_123_456, 48_000, 480, 1, 1, 0x12345678);
        Span<byte> bytes = stackalloc byte[AudioPacketHeader.Size];
        expected.Write(bytes);
        Assert.True(AudioPacketHeader.TryParse(bytes, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal("414D4943010000200000002A000620F824B59A400000BB8001E0010112345678", Convert.ToHexString(bytes));
    }

    [Theory]
    [InlineData(8_000)]
    [InlineData(44_100)]
    public void HeaderRejectsUnsupportedRates(uint rate)
    {
        var header = new AudioPacketHeader(1, 1, 1, rate, 480, 1, 1, 1);
        Span<byte> bytes = stackalloc byte[AudioPacketHeader.Size];
        header.Write(bytes);
        Assert.False(AudioPacketHeader.TryParse(bytes, out _));
    }

    [Fact]
    public void EncryptedPacketRoundTripsAndRejectsTampering()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var pcm = Enumerable.Range(0, 960).Select(i => (byte)(i % 251)).ToArray();
        var header = new AudioPacketHeader(1, 7, 99, 48_000, 480, 1, 1, 1234);
        using var cipher = new AudioPacketCipher(key);
        var datagram = cipher.Encrypt(header, pcm);
        Assert.True(cipher.TryDecrypt(datagram, 1234, out var parsed, out var decrypted));
        Assert.Equal(header, parsed);
        Assert.Equal(pcm, decrypted);

        datagram[40] ^= 1;
        Assert.False(cipher.TryDecrypt(datagram, 1234, out _, out _));
    }

    [Fact]
    public void SequenceWindowRejectsReplayAndOldPackets()
    {
        var window = new SequenceWindow();
        Assert.True(window.TryAccept(100));
        Assert.False(window.TryAccept(100));
        Assert.True(window.TryAccept(102));
        Assert.True(window.TryAccept(101));
        Assert.False(window.TryAccept(1));
    }
}
