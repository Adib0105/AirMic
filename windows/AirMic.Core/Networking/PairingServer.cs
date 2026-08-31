using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AirMic.Core.Security;

namespace AirMic.Core.Networking;

public sealed class PairingServer : IAsyncDisposable
{
    public const int DefaultControlPort = 51243;
    public const int DefaultAudioPort = 51244;
    private readonly TcpListener _listener;
    private readonly PairingPinService _pins;
    private readonly X509Certificate2 _certificate;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;

    public event EventHandler<PairedSession>? SessionEstablished;

    public PairingServer(PairingPinService pins, X509Certificate2 certificate, int port = DefaultControlPort)
    {
        _pins = pins;
        _certificate = certificate;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _listener.Start(8);
        _acceptLoop = AcceptLoopAsync(_lifetime.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
            if (endpoint is null || !NetworkPolicy.IsTrustedLan(endpoint.Address)) return;

            using var tls = new SslStream(client.GetStream(), false);
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false
                }, cancellationToken).ConfigureAwait(false);

                var request = await FramedJson.ReadAsync<PairingRequest>(tls, cancellationToken).ConfigureAwait(false);
                if (request is null || request.Type != "pair" || request.ProtocolVersion != 1 ||
                    string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.DeviceName))
                {
                    await FramedJson.WriteAsync(tls, new PairingResponse(false, "Unsupported pairing request.", null, null, null, null, null), cancellationToken);
                    return;
                }

                if (!_pins.TryValidate(endpoint.Address.ToString(), request.Pin, out var retryAfter))
                {
                    var message = retryAfter > TimeSpan.Zero
                        ? $"Too many attempts. Try again in {Math.Ceiling(retryAfter.TotalSeconds)} seconds."
                        : "The pairing PIN is incorrect.";
                    await FramedJson.WriteAsync(tls, new PairingResponse(false, message, null, null, null, null, null), cancellationToken);
                    return;
                }

                var key = RandomNumberGenerator.GetBytes(32);
                var sessionId = RandomUInt32NonZero();
                var session = new PairedSession(sessionId, key, endpoint.Address,
                    request.DeviceId[..Math.Min(request.DeviceId.Length, 128)],
                    request.DeviceName[..Math.Min(request.DeviceName.Length, 128)], DateTimeOffset.UtcNow);
                await FramedJson.WriteAsync(tls, new PairingResponse(true, null, sessionId, DefaultAudioPort,
                    $"{Environment.MachineName}.local", Convert.ToBase64String(key), ReceiverCertificateStore.Fingerprint(_certificate)), cancellationToken);
                SessionEstablished?.Invoke(this, session);
                _pins.Rotate();
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException or JsonException or OperationCanceledException)
            {
                // Expected malformed/aborted client. The UI logger records only the exception type.
            }
        }
    }

    private static uint RandomUInt32NonZero()
    {
        uint value;
        do value = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint)));
        while (value == 0);
        return value;
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is null) return;
        await _lifetime.CancelAsync();
        _listener.Stop();
        if (_acceptLoop is not null)
            try { await _acceptLoop.ConfigureAwait(false); } catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        _lifetime.Dispose();
        _lifetime = null;
        _certificate.Dispose();
    }
}
