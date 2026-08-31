using AirMic.Core.Audio;
using AirMic.Core.Diagnostics;
using AirMic.Core.Discovery;
using AirMic.Core.Networking;
using AirMic.Core.Security;
using AirMic.Core.VirtualDevice;

namespace AirMic.Windows;

public sealed class ReceiverCoordinator : IAsyncDisposable
{
    private readonly PairingPinService _pins = new();
    private readonly PairingServer _pairing;
    private readonly AudioUdpReceiver _receiver = new();
    private readonly MdnsAdvertiser _mdns;
    public ConnectionDiagnostics Diagnostics { get; } = new();
    public StructuredLog Log { get; } = new();
    public AudioSessionEngine Audio { get; }
    public string PairingPin => _pins.CurrentPin;
    public string ConnectedDevice { get; private set; } = "Waiting for iPhone";
    public bool IsConnected { get; private set; }
    public event EventHandler? StateChanged;
    public event EventHandler<float>? LevelChanged;

    public ReceiverCoordinator()
    {
        var certificate = new ReceiverCertificateStore().LoadOrCreate();
        _pairing = new PairingServer(_pins, certificate);
        _pairing.SessionEstablished += OnSessionEstablished;
        _mdns = new MdnsAdvertiser($"{Environment.MachineName} AirMic", PairingServer.DefaultControlPort);
        Audio = new AudioSessionEngine(_receiver, new VirtualMicrophoneSink(), new PreviewAudioSink(), Diagnostics);
        Audio.LevelChanged += (_, level) => LevelChanged?.Invoke(this, level);
    }

    public async Task StartAsync()
    {
        _pairing.Start();
        _mdns.Start();
        Audio.Start();
        Diagnostics.LocalNetworkReachable = true;
        await Log.WriteAsync("info", "receiver_started", new { controlPort = 51243, audioPort = 51244 });
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnSessionEstablished(object? sender, PairedSession session)
    {
        _receiver.SetSession(session);
        ConnectedDevice = session.DeviceName;
        IsConnected = true;
        Diagnostics.IphoneDiscovered = true;
        Diagnostics.PairingSuccessful = true;
        Diagnostics.SetDevice(session.DeviceName);
        await Log.WriteAsync("info", "pairing_succeeded", new { deviceName = session.DeviceName });
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        Audio.Disconnect();
        ConnectedDevice = "Waiting for iPhone";
        IsConnected = false;
        Diagnostics.PairingSuccessful = false;
        await Log.WriteAsync("info", "session_disconnected");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string CopyableDiagnostics() => System.Text.Json.JsonSerializer.Serialize(Diagnostics.Snapshot(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public async ValueTask DisposeAsync()
    {
        _pairing.SessionEstablished -= OnSessionEstablished;
        await Audio.DisposeAsync();
        await _mdns.DisposeAsync();
        await _pairing.DisposeAsync();
    }
}
