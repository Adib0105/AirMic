using System.Net;

namespace AirMic.Core.Networking;

public sealed record PairingRequest(string Type, int ProtocolVersion, string DeviceId, string DeviceName, string Pin);
public sealed record PairingResponse(bool Ok, string? Error, uint? SessionId, int? AudioPort, string? AudioHost, string? Key, string? CertificateFingerprint);

public sealed record PairedSession(
    uint SessionId,
    byte[] Key,
    IPAddress RemoteAddress,
    string DeviceId,
    string DeviceName,
    DateTimeOffset PairedAt) : IDisposable
{
    public void Dispose() => System.Security.Cryptography.CryptographicOperations.ZeroMemory(Key);
}
