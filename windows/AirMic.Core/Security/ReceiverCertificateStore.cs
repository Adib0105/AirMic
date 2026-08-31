using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AirMic.Core.Security;

public sealed class ReceiverCertificateStore
{
    private readonly string _path;

    public ReceiverCertificateStore(string? applicationData = null)
    {
        var root = applicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _path = Path.Combine(root, "AirMic", "receiver.pfx");
    }

    public X509Certificate2 LoadOrCreate()
    {
        if (File.Exists(_path))
            return new X509Certificate2(_path, (string?)null,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=AirMic Receiver", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Environment.MachineName);
        san.AddDnsName("airmic.local");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        var pfx = created.Export(X509ContentType.Pfx);
        File.WriteAllBytes(_path, pfx);
        return new X509Certificate2(pfx, (string?)null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }

    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
}
