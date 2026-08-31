using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AirMic.Core.VirtualDevice;

public sealed class VirtualMicrophoneSink : IAudioSink
{
    public const string RenderEndpointName = "AirMic Network Input";
    public const string CaptureEndpointName = "AirMic Virtual Microphone";
    private readonly object _gate = new();
    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;
    private MMDevice? _device;
    private int _sampleRate;
    private string _status = "Driver not detected";

    public bool IsAvailable { get; private set; }
    public string Status => _status;

    public bool Refresh()
    {
        lock (_gate)
        {
            using var enumerator = new MMDeviceEnumerator();
            _device?.Dispose();
            _device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Contains(RenderEndpointName, StringComparison.OrdinalIgnoreCase));
            IsAvailable = _device is not null;
            _status = IsAvailable ? $"Ready: {CaptureEndpointName}" : "AirMic audio driver is not installed";
            return IsAvailable;
        }
    }

    public void Write(ReadOnlySpan<byte> pcm16, int sampleRate, int channels)
    {
        if (channels != 1) throw new NotSupportedException("AirMic v1 supports mono audio.");
        lock (_gate)
        {
            if (!IsAvailable && !Refresh()) throw new InvalidOperationException(_status);
            EnsureStarted(sampleRate);
            var bytes = pcm16.ToArray();
            _buffer!.AddSamples(bytes, 0, bytes.Length);
        }
    }

    private void EnsureStarted(int sampleRate)
    {
        if (_output is not null && _sampleRate == sampleRate) return;
        StopUnsafe();
        if (_device is null && !Refresh()) throw new InvalidOperationException(_status);
        _sampleRate = sampleRate;
        _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromMilliseconds(200),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        _output = new WasapiOut(_device!, AudioClientShareMode.Shared, true, 20);
        _output.Init(_buffer);
        _output.Play();
        _status = $"Streaming to {CaptureEndpointName}";
    }

    public void Stop() { lock (_gate) StopUnsafe(); }
    private void StopUnsafe() { _output?.Stop(); _output?.Dispose(); _output = null; _buffer = null; }
    public void Dispose() { lock (_gate) { StopUnsafe(); _device?.Dispose(); _device = null; } }
}
