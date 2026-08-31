using NAudio.Wave;

namespace AirMic.Core.VirtualDevice;

public sealed class PreviewAudioSink : IAudioSink
{
    private readonly object _gate = new();
    private WaveOutEvent? _output;
    private BufferedWaveProvider? _buffer;
    private int _sampleRate;

    public bool IsAvailable => true;
    public string Status => _output is null ? "Stopped" : "Playing preview";

    public void Write(ReadOnlySpan<byte> pcm16, int sampleRate, int channels)
    {
        if (channels != 1) throw new NotSupportedException("AirMic v1 supports mono audio.");
        lock (_gate)
        {
            EnsureStarted(sampleRate);
            var bytes = pcm16.ToArray();
            _buffer!.AddSamples(bytes, 0, bytes.Length);
        }
    }

    private void EnsureStarted(int sampleRate)
    {
        if (_output is not null && _sampleRate == sampleRate) return;
        StopUnsafe();
        _sampleRate = sampleRate;
        _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromMilliseconds(250),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        _output = new WaveOutEvent { DesiredLatency = 50, NumberOfBuffers = 3 };
        _output.Init(_buffer);
        _output.Play();
    }

    public void Stop() { lock (_gate) StopUnsafe(); }
    private void StopUnsafe() { _output?.Stop(); _output?.Dispose(); _output = null; _buffer = null; }
    public void Dispose() => Stop();
}
