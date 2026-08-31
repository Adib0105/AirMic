namespace AirMic.Core.Diagnostics;

public sealed record DiagnosticsSnapshot(
    bool LocalNetworkReachable,
    bool IphoneDiscovered,
    bool PairingSuccessful,
    bool AudioStreamActive,
    bool VirtualMicrophoneAvailable,
    int SampleRate,
    int Channels,
    double PacketsPerSecond,
    double LatencyMilliseconds,
    double PacketLossPercent,
    string ConnectedDevice,
    DateTimeOffset CapturedAt);

public sealed class ConnectionDiagnostics
{
    private readonly object _gate = new();
    private long _packets;
    private long _missing;
    private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;
    private double _lastPacketsPerSecond;
    private double _lastPacketLossPercent;
    private double _latencyMs;
    private int _sampleRate;
    private string _device = "None";

    public bool LocalNetworkReachable { get; set; }
    public bool IphoneDiscovered { get; set; }
    public bool PairingSuccessful { get; set; }
    public bool AudioStreamActive { get; set; }
    public bool VirtualMicrophoneAvailable { get; set; }

    public void SetDevice(string name) { lock (_gate) _device = name; }

    public void ObservePacket(ulong captureTimestampMicros, int sampleRate)
    {
        lock (_gate)
        {
            _packets++;
            _sampleRate = sampleRate;
            var arrivalMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
            var sample = Math.Clamp((arrivalMicros - (long)captureTimestampMicros) / 1000d, 0, 2000);
            _latencyMs = _latencyMs == 0 ? sample : _latencyMs * 0.9 + sample * 0.1;
            RotateWindowUnsafe();
        }
    }

    public void ObserveMissingPacket() { lock (_gate) { _missing++; RotateWindowUnsafe(); } }

    public DiagnosticsSnapshot Snapshot()
    {
        lock (_gate)
        {
            RotateWindowUnsafe();
            return new DiagnosticsSnapshot(LocalNetworkReachable, IphoneDiscovered, PairingSuccessful,
                AudioStreamActive, VirtualMicrophoneAvailable, _sampleRate, 1, _lastPacketsPerSecond,
                _latencyMs, _lastPacketLossPercent, _device, DateTimeOffset.UtcNow);
        }
    }

    private void RotateWindowUnsafe()
    {
        var elapsed = DateTimeOffset.UtcNow - _windowStart;
        if (elapsed < TimeSpan.FromSeconds(1)) return;
        _lastPacketsPerSecond = _packets / elapsed.TotalSeconds;
        var total = _packets + _missing;
        _lastPacketLossPercent = total == 0 ? 0 : _missing * 100d / total;
        _packets = 0;
        _missing = 0;
        _windowStart = DateTimeOffset.UtcNow;
    }
}
