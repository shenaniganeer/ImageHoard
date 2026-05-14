using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace ImageHoard.Core.Metrics;

/// <summary>FR-BR-07 — simple JSONL cache rows keyed by path.</summary>
public static class FolderMetricsCacheStore
{
    /// <summary>Serializes JSONL appends (and <see cref="ClearCacheFile"/>) so concurrent folder-metrics jobs cannot open the same path for exclusive append.</summary>
    private static readonly SemaphoreSlim MetricsCacheFileGate = new(1, 1);

    /// <summary>Guards tail read cache so concurrent metrics workers reuse one tail parse per file revision.</summary>
    private static readonly SemaphoreSlim TailTextCacheGate = new(1, 1);

    private static TailTextCacheEntry? s_tailTextCache;

    private sealed class TailTextCacheEntry
    {
        public required string CacheFilePath;
        public long FileLength;
        public long LastWriteUtcTicks;
        public required string Text;
    }

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
            InvalidateTailTextCacheIfPathMatches(cacheFilePath);
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
        {
            if (!string.IsNullOrEmpty(cacheFilePath))
                InvalidateTailTextCacheIfPathMatches(cacheFilePath);
            return null;
        }

        var key = NormalizePathKey(directoryPath);
        if (string.IsNullOrEmpty(key))
            return null;

        await TailTextCacheGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryGetTailTextFromCache(cacheFilePath, out var cached))
                return FindSnapshotInTailText(cached, key, requestedScope, ct);
        }
        finally
        {
            TailTextCacheGate.Release();
        }

        var readText = await ReadTailTextAsync(cacheFilePath, ct).ConfigureAwait(false);

        await TailTextCacheGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryGetTailTextFromCache(cacheFilePath, out var cached))
                return FindSnapshotInTailText(cached, key, requestedScope, ct);
            RememberTailTextCache(cacheFilePath, readText);
        }
        finally
        {
            TailTextCacheGate.Release();
        }

        return FindSnapshotInTailText(readText, key, requestedScope, ct);
    }

    private static FolderMetricsSnapshot? FindSnapshotInTailText(
        string text,
        string normalizedDirectoryKey,
        FolderMetricsScanScope requestedScope,
        CancellationToken ct)
    {
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
            if (!PathsEqual(normalizedDirectoryKey, snap.Path))
                continue;
            if (snap.ScanScope != requestedScope)
                continue;
            return snap;
        }

        return null;
    }

    private static bool TryGetTailTextFromCache(string cacheFilePath, out string text)
    {
        text = "";
        if (s_tailTextCache == null)
            return false;
        if (!PathsEqual(s_tailTextCache.CacheFilePath, cacheFilePath))
            return false;

        long len;
        long ticks;
        try
        {
            var fi = new FileInfo(cacheFilePath);
            len = fi.Length;
            ticks = fi.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return false;
        }

        if (len != s_tailTextCache.FileLength || ticks != s_tailTextCache.LastWriteUtcTicks)
            return false;

        text = s_tailTextCache.Text;
        return true;
    }

    private static void RememberTailTextCache(string cacheFilePath, string text)
    {
        try
        {
            var fi = new FileInfo(cacheFilePath);
            s_tailTextCache = new TailTextCacheEntry
            {
                CacheFilePath = cacheFilePath,
                FileLength = fi.Length,
                LastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks,
                Text = text,
            };
        }
        catch
        {
            s_tailTextCache = null;
        }
    }

    private static async Task<string> ReadTailTextAsync(string cacheFilePath, CancellationToken ct)
    {
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
            return "";

        var take = (int)Math.Min(len, tailMaxBytes);
        var start = len - take;
        fs.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[take];
        var read = await fs.ReadAsync(buffer.AsMemory(0, take), ct).ConfigureAwait(false);
        if (read == 0)
            return "";

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        if (start > 0)
        {
            var firstNl = text.IndexOfAny(['\r', '\n']);
            if (firstNl >= 0)
                text = text[(firstNl + 1)..];
        }

        return text;
    }

    private static void InvalidateTailTextCacheIfPathMatches(string cacheFilePath)
    {
        if (string.IsNullOrEmpty(cacheFilePath))
            return;
        TailTextCacheGate.Wait();
        try
        {
            if (s_tailTextCache != null && PathsEqual(s_tailTextCache.CacheFilePath, cacheFilePath))
                s_tailTextCache = null;
        }
        finally
        {
            TailTextCacheGate.Release();
        }
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
            InvalidateTailTextCacheIfPathMatches(cacheFilePath);
        }
        finally
        {
            MetricsCacheFileGate.Release();
        }
    }
}
