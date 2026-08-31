using AirMic.Core.Protocol;

namespace AirMic.Core.Audio;

public sealed record AudioFrame(AudioPacketHeader Header, byte[] Pcm, DateTimeOffset ReceivedAt)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Header.SampleCount / Header.SampleRate);
}
