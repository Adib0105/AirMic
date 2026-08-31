namespace AirMic.Core.VirtualDevice;

public interface IAudioSink : IDisposable
{
    bool IsAvailable { get; }
    string Status { get; }
    void Write(ReadOnlySpan<byte> pcm16, int sampleRate, int channels);
    void Stop();
}
