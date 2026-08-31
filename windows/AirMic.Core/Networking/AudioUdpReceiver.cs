using System.Net;
using System.Net.Sockets;
using AirMic.Core.Audio;
using AirMic.Core.Security;

namespace AirMic.Core.Networking;

public sealed class AudioUdpReceiver : IAsyncDisposable
{
    private readonly UdpClient _udp;
    private readonly object _sessionGate = new();
    private CancellationTokenSource? _lifetime;
    private Task? _receiveLoop;
    private PairedSession? _session;
    private AudioPacketCipher? _cipher;
    private SequenceWindow _sequenceWindow = new();

    public event EventHandler<AudioFrame>? FrameReceived;
    public event EventHandler? SessionChanged;
    public long RejectedPackets { get; private set; }

    public AudioUdpReceiver(int port = PairingServer.DefaultAudioPort)
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _udp.Client.ReceiveBufferSize = 512 * 1024;
    }

    public void SetSession(PairedSession session)
    {
        lock (_sessionGate)
        {
            _cipher?.Dispose();
            _session?.Dispose();
            _session = session;
            _cipher = new AudioPacketCipher(session.Key);
            _sequenceWindow = new SequenceWindow();
        }
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Start()
    {
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _receiveLoop = ReceiveLoopAsync(_lifetime.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _udp.ReceiveAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }

            AudioFrame? frame = null;
            lock (_sessionGate)
            {
                if (_session is null || _cipher is null || !_session.RemoteAddress.Equals(result.RemoteEndPoint.Address) ||
                    !_cipher.TryDecrypt(result.Buffer, _session.SessionId, out var header, out var pcm) ||
                    !_sequenceWindow.TryAccept(header.Sequence))
                {
                    RejectedPackets++;
                }
                else
                {
                    frame = new AudioFrame(header, pcm, DateTimeOffset.UtcNow);
                }
            }
            if (frame is not null) FrameReceived?.Invoke(this, frame);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync();
            _udp.Dispose();
            if (_receiveLoop is not null)
                try { await _receiveLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _lifetime.Dispose();
        }
        lock (_sessionGate)
        {
            _cipher?.Dispose();
            _session?.Dispose();
        }
    }
}
