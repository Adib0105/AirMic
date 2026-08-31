using AirMic.Core.Diagnostics;
using AirMic.Core.Networking;
using AirMic.Core.VirtualDevice;

namespace AirMic.Core.Audio;

public sealed class AudioSessionEngine : IAsyncDisposable
{
    private readonly AudioUdpReceiver _receiver;
    private readonly AdaptiveJitterBuffer _jitter = new();
    private readonly VirtualMicrophoneSink _virtualSink;
    private readonly PreviewAudioSink _previewSink;
    private readonly ConnectionDiagnostics _diagnostics;
    private CancellationTokenSource? _lifetime;
    private Task? _playoutLoop;
    private ushort _lastSamples = 480;
    private uint _lastRate = 48000;

    public float Gain { get; set; } = 1f;
    public float NoiseGateThresholdDb { get; set; } = -55f;
    public bool Muted { get; set; }
    public bool PreviewEnabled { get; set; }
    public event EventHandler<float>? LevelChanged;

    public AudioSessionEngine(AudioUdpReceiver receiver, VirtualMicrophoneSink virtualSink,
        PreviewAudioSink previewSink, ConnectionDiagnostics diagnostics)
    {
        _receiver = receiver;
        _virtualSink = virtualSink;
        _previewSink = previewSink;
        _diagnostics = diagnostics;
        _receiver.FrameReceived += OnFrameReceived;
    }

    public void Start()
    {
        if (_lifetime is not null) return;
        _virtualSink.Refresh();
        _diagnostics.VirtualMicrophoneAvailable = _virtualSink.IsAvailable;
        _receiver.Start();
        _lifetime = new CancellationTokenSource();
        _playoutLoop = PlayoutLoopAsync(_lifetime.Token);
    }

    private void OnFrameReceived(object? sender, AudioFrame frame)
    {
        _jitter.Push(frame);
        _diagnostics.AudioStreamActive = true;
        _diagnostics.ObservePacket(frame.Header.TimestampMicros, (int)frame.Header.SampleRate);
    }

    private async Task PlayoutLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!_jitter.TryRead(out var frame, out var missing)) continue;
            byte[] pcm;
            if (frame is not null)
            {
                _lastSamples = frame.Header.SampleCount;
                _lastRate = frame.Header.SampleRate;
                pcm = frame.Pcm;
            }
            else
            {
                pcm = new byte[_lastSamples * 2];
            }

            if (missing) _diagnostics.ObserveMissingPacket();
            if (Muted) Array.Clear(pcm);
            else PcmProcessor.ApplyGainAndGate(pcm, Gain, NoiseGateThresholdDb);
            LevelChanged?.Invoke(this, Muted ? 0 : PcmProcessor.Peak(pcm));

            if (_virtualSink.IsAvailable)
            {
                try { _virtualSink.Write(pcm, (int)_lastRate, 1); }
                catch (InvalidOperationException) { _diagnostics.VirtualMicrophoneAvailable = false; }
            }
            if (PreviewEnabled) _previewSink.Write(pcm, (int)_lastRate, 1);
        }
    }

    public void Disconnect()
    {
        _jitter.Reset();
        _virtualSink.Stop();
        _previewSink.Stop();
        _diagnostics.AudioStreamActive = false;
        LevelChanged?.Invoke(this, 0);
    }

    public async ValueTask DisposeAsync()
    {
        _receiver.FrameReceived -= OnFrameReceived;
        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync();
            if (_playoutLoop is not null)
                try { await _playoutLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _lifetime.Dispose();
        }
        _virtualSink.Dispose();
        _previewSink.Dispose();
        await _receiver.DisposeAsync();
    }
}
