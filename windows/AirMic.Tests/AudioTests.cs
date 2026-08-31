using System.Buffers.Binary;
using AirMic.Core.Audio;
using AirMic.Core.Protocol;

namespace AirMic.Tests;

public sealed class AudioTests
{
    [Fact]
    public void GainClipsAndGateMutesQuietSamples()
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0, 2), 10);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2, 2), 20_000);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4, 2), -20_000);
        PcmProcessor.ApplyGainAndGate(bytes, 2, -60);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0, 2)));
        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2, 2)));
        Assert.Equal(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4, 2)));
    }

    [Fact]
    public void ResamplerPreservesEndpoints()
    {
        short[] input = [0, 1_000, 2_000, 3_000];
        var output = PcmProcessor.ResampleLinear(input, 24_000, 48_000);
        Assert.Equal(8, output.Length);
        Assert.Equal(0, output[0]);
        Assert.InRange(output[^1], 2_900, 3_000);
    }

    [Fact]
    public void JitterBufferReordersAndSignalsLoss()
    {
        var buffer = new AdaptiveJitterBuffer(2);
        buffer.Push(Frame(11));
        buffer.Push(Frame(10));
        Assert.True(buffer.TryRead(out var first, out var missing));
        Assert.False(missing);
        Assert.Equal(10u, first!.Header.Sequence);
        buffer.Push(Frame(13));
        Assert.True(buffer.TryRead(out var second, out missing));
        Assert.False(missing);
        Assert.Equal(11u, second!.Header.Sequence);
        buffer.Push(Frame(14));
        Assert.True(buffer.TryRead(out var absent, out missing));
        Assert.True(missing);
        Assert.Null(absent);
    }

    private static AudioFrame Frame(uint sequence) => new(
        new AudioPacketHeader(1, sequence, 1, 48_000, 480, 1, 1, 1), new byte[960], DateTimeOffset.UtcNow);
}
