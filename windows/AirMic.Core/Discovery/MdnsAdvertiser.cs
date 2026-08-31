using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace AirMic.Core.Discovery;

public sealed class MdnsAdvertiser : IAsyncDisposable
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("224.0.0.251"), 5353);
    private readonly UdpClient _udp;
    private readonly string _instanceName;
    private readonly string _hostName;
    private readonly int _controlPort;
    private CancellationTokenSource? _lifetime;
    private Task? _loop;

    public MdnsAdvertiser(string displayName, int controlPort)
    {
        _controlPort = controlPort;
        var safeHost = new string(Environment.MachineName.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(safeHost)) safeHost = "airmic-pc";
        _hostName = $"{safeHost}.local";
        _instanceName = $"{SanitizeLabel(displayName)}._airmic._tcp.local";

        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.ExclusiveAddressUse = false;
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
        _udp.JoinMulticastGroup(MulticastEndpoint.Address);
    }

    public void Start()
    {
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _loop = Task.WhenAll(ReceiveLoopAsync(_lifetime.Token), AnnounceLoopAsync(_lifetime.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var query = await _udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (ContainsAscii(query.Buffer, "_airmic._tcp")) await AnnounceAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
    {
        await AnnounceAsync(cancellationToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            await AnnounceAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AnnounceAsync(CancellationToken cancellationToken)
    {
        var addresses = GetPrivateIpv4Addresses().ToArray();
        if (addresses.Length == 0) return;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        WriteUInt16(writer, 0); // transaction ID
        WriteUInt16(writer, 0x8400); // response + authoritative
        WriteUInt16(writer, 0); // questions
        WriteUInt16(writer, checked((ushort)(3 + addresses.Length)));
        WriteUInt16(writer, 0);
        WriteUInt16(writer, 0);

        WriteRecord(writer, "_airmic._tcp.local", 12, 1, 120, payload => WriteName(payload, _instanceName));
        WriteRecord(writer, _instanceName, 33, 0x8001, 120, payload =>
        {
            WriteUInt16(payload, 0); WriteUInt16(payload, 0); WriteUInt16(payload, checked((ushort)_controlPort));
            WriteName(payload, _hostName);
        });
        WriteRecord(writer, _instanceName, 16, 0x8001, 120, payload =>
        {
            WriteTxt(payload, "version=1");
            WriteTxt(payload, $"controlPort={_controlPort}");
            WriteTxt(payload, $"name={Environment.MachineName}");
        });
        foreach (var address in addresses)
            WriteRecord(writer, _hostName, 1, 0x8001, 120, payload => payload.Write(address.GetAddressBytes()));

        var packet = stream.ToArray();
        await _udp.SendAsync(packet, MulticastEndpoint, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<IPAddress> GetPrivateIpv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && Networking.NetworkPolicy.IsTrustedLan(a))
            .Distinct();

    private static void WriteRecord(BinaryWriter writer, string name, ushort type, ushort @class, uint ttl, Action<BinaryWriter> writePayload)
    {
        WriteName(writer, name);
        WriteUInt16(writer, type);
        WriteUInt16(writer, @class);
        WriteUInt32(writer, ttl);
        using var payloadStream = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, true)) writePayload(payloadWriter);
        var payload = payloadStream.ToArray();
        WriteUInt16(writer, checked((ushort)payload.Length));
        writer.Write(payload);
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new InvalidDataException("Invalid mDNS label.");
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        writer.Write((byte)0);
    }

    private static void WriteTxt(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 255) throw new InvalidDataException("mDNS TXT value is too long.");
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }

    private static string SanitizeLabel(string value)
    {
        var clean = new string(value.Where(c => c >= 0x20 && c != '.').Take(50).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "AirMic PC" : clean;
    }

    private static bool ContainsAscii(ReadOnlySpan<byte> bytes, string value) =>
        Encoding.ASCII.GetString(bytes).Contains(value, StringComparison.OrdinalIgnoreCase);

    private static void WriteUInt16(BinaryWriter writer, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, value); writer.Write(bytes);
    }
    private static void WriteUInt32(BinaryWriter writer, uint value)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); writer.Write(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is null) return;
        await _lifetime.CancelAsync();
        _udp.Dispose();
        if (_loop is not null)
            try { await _loop.ConfigureAwait(false); } catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { }
        _lifetime.Dispose();
        _lifetime = null;
    }
}
