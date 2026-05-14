using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageHoard.Core.Metrics;

/// <summary>FR-BR-07 — simple JSONL cache rows keyed by path.</summary>
public static class FolderMetricsCacheStore
{
    /// <summary>Serializes JSONL appends (and <see cref="ClearCacheFile"/>) so concurrent folder-metrics jobs cannot open the same path for exclusive append.</summary>
    private static readonly SemaphoreSlim MetricsCacheFileGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task AppendSnapshotAsync(string cacheFilePath, FolderMetricsSnapshot snapshot, CancellationToken ct = default)
    {
        await MetricsCacheFileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var line = JsonSerializer.Serialize(snapshot, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(cacheFilePath, line, ct).ConfigureAwait(false);
        }
        finally
        {
            MetricsCacheFileGate.Release();
        }
    }

    /// <summary>
    /// Returns the last JSONL row for <paramref name="directoryPath"/> by scanning the tail of the file (bounded read).
    /// </summary>
    public static async Task<FolderMetricsSnapshot?> TryGetLatestSnapshotForPathAsync(
        string cacheFilePath,
        string directoryPath,
        FolderMetricsScanScope requestedScope = FolderMetricsScanScope.FullSubtree,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(cacheFilePath) || !File.Exists(cacheFilePath))
            return null;

        var key = NormalizePathKey(directoryPath);
        if (string.IsNullOrEmpty(key))
            return null;

        const int tailMaxBytes = 524_288;
        await using var fs = new FileStream(
            cacheFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);

        var len = fs.Length;
        if (len == 0)
            return null;

        var take = (int)Math.Min(len, tailMaxBytes);
        var start = len - take;
        fs.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[take];
        var read = await fs.ReadAsync(buffer.AsMemory(0, take), ct).ConfigureAwait(false);
        if (read == 0)
            return null;

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        if (start > 0)
        {
            var firstNl = text.IndexOfAny(['\r', '\n']);
            if (firstNl >= 0)
                text = text[(firstNl + 1)..];
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;
            FolderMetricsSnapshot? snap;
            try
            {
                snap = JsonSerializer.Deserialize<FolderMetricsSnapshot>(line, JsonOptions);
            }
            catch
            {
                continue;
            }

            if (snap == null)
                continue;
            if (!PathsEqual(key, snap.Path))
                continue;
            if (snap.ScanScope != requestedScope)
                continue;
            return snap;
        }

        return null;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePathKey(string path)
    {
        try
        {
            return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static void ClearCacheFile(string cacheFilePath)
    {
        MetricsCacheFileGate.Wait();
        try
        {
            if (File.Exists(cacheFilePath))
                File.Delete(cacheFilePath);
        }
        finally
        {
            MetricsCacheFileGate.Release();
        }
    }
}
