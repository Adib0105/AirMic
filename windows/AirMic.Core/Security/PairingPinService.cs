using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AirMic.Core.Security;

public sealed class PairingPinService
{
    private sealed record AttemptWindow(Queue<DateTimeOffset> Failures, DateTimeOffset LockedUntil);
    private readonly ConcurrentDictionary<string, AttemptWindow> _attempts = new(StringComparer.Ordinal);
    private readonly object _pinGate = new();
    private string _pin = CreatePin();
    private DateTimeOffset _expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

    public string CurrentPin
    {
        get
        {
            lock (_pinGate)
            {
                if (DateTimeOffset.UtcNow >= _expiresAt) RotateUnsafe();
                return _pin;
            }
        }
    }

    public DateTimeOffset ExpiresAt { get { lock (_pinGate) return _expiresAt; } }

    public void Rotate()
    {
        lock (_pinGate) RotateUnsafe();
    }

    public bool TryValidate(string source, string candidate, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = DateTimeOffset.UtcNow;
        var window = _attempts.GetOrAdd(source, _ => new AttemptWindow(new Queue<DateTimeOffset>(), DateTimeOffset.MinValue));
        lock (window)
        {
            if (window.LockedUntil > now)
            {
                retryAfter = window.LockedUntil - now;
                return false;
            }
            while (window.Failures.TryPeek(out var first) && now - first > TimeSpan.FromMinutes(5)) window.Failures.Dequeue();

            string current;
            lock (_pinGate)
            {
                if (now >= _expiresAt) RotateUnsafe();
                current = _pin;
            }

            var validShape = candidate.Length == 6 && candidate.All(char.IsAsciiDigit);
            var ok = validShape && CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(current), System.Text.Encoding.ASCII.GetBytes(candidate));
            if (ok)
            {
                window.Failures.Clear();
                return true;
            }

            window.Failures.Enqueue(now);
            if (window.Failures.Count >= 5)
            {
                window = window with { LockedUntil = now.AddSeconds(30) };
                _attempts[source] = window;
                retryAfter = TimeSpan.FromSeconds(30);
            }
            return false;
        }
    }

    private void RotateUnsafe()
    {
        _pin = CreatePin();
        _expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static string CreatePin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
