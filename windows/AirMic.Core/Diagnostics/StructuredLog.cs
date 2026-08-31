using System.Text.Json;

namespace AirMic.Core.Diagnostics;

public sealed class StructuredLog
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StructuredLog(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirMic", "logs");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, $"airmic-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    public async Task WriteAsync(string level, string eventName, object? properties = null)
    {
        var entry = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            level,
            eventName,
            properties
        });
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await File.AppendAllTextAsync(_path, entry + Environment.NewLine).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
