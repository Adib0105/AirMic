using System.Buffers.Binary;

namespace AirMic.Core.Audio;

public static class PcmProcessor
{
    public static float Peak(ReadOnlySpan<byte> pcm16)
    {
        var peak = 0;
        for (var i = 0; i + 1 < pcm16.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm16[i..])));
        return peak / 32768f;
    }

    public static void ApplyGainAndGate(Span<byte> pcm16, float gain, float gateThresholdDb)
    {
        gain = Math.Clamp(gain, 0f, 3f);
        var threshold = MathF.Pow(10f, Math.Clamp(gateThresholdDb, -90f, -10f) / 20f) * short.MaxValue;
        for (var i = 0; i + 1 < pcm16.Length; i += 2)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(pcm16[i..]);
            var processed = Math.Abs((int)value) < threshold ? 0 : (int)MathF.Round(value * gain);
            BinaryPrimitives.WriteInt16LittleEndian(pcm16[i..], (short)Math.Clamp(processed, short.MinValue, short.MaxValue));
        }
    }

    public static short[] ResampleLinear(ReadOnlySpan<short> input, int sourceRate, int destinationRate)
    {
        if (sourceRate <= 0 || destinationRate <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRate));
        if (input.IsEmpty) return [];
        if (sourceRate == destinationRate) return input.ToArray();
        var outputLength = Math.Max(1, (int)Math.Round(input.Length * (double)destinationRate / sourceRate));
        var output = new short[outputLength];
        var scale = (double)sourceRate / destinationRate;
        for (var i = 0; i < output.Length; i++)
        {
            var position = i * scale;
            var left = Math.Min((int)position, input.Length - 1);
            var right = Math.Min(left + 1, input.Length - 1);
            var fraction = position - left;
            output[i] = (short)Math.Clamp((int)Math.Round(input[left] + (input[right] - input[left]) * fraction), short.MinValue, short.MaxValue);
        }
        return output;
    }
}
