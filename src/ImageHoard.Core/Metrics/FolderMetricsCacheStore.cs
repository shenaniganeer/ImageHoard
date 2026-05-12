using System.Text.Json;

namespace ImageHoard.Core.Metrics;

/// <summary>FR-BR-07 — simple JSONL cache rows keyed by path.</summary>
public static class FolderMetricsCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static async Task AppendSnapshotAsync(string cacheFilePath, FolderMetricsSnapshot snapshot, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(cacheFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var line = JsonSerializer.Serialize(snapshot, JsonOptions) + Environment.NewLine;
        await File.AppendAllTextAsync(cacheFilePath, line, ct).ConfigureAwait(false);
    }

    public static void ClearCacheFile(string cacheFilePath)
    {
        if (File.Exists(cacheFilePath))
            File.Delete(cacheFilePath);
    }
}
