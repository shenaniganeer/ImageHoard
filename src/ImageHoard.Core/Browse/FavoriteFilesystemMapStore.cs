using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ImageHoard.Core.Metrics;

namespace ImageHoard.Core.Browse;

/// <summary>
/// Persists per-directory subtree metrics for a single favorite index root (deduped nested favorites share one file).
/// </summary>
public static class FavoriteFilesystemMapStore
{
    private static readonly SemaphoreSlim FileGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string MapFilePathForIndexRoot(string mapsDirectory, string indexRoot)
    {
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(norm)));
        return Path.Combine(mapsDirectory, $"favorite-fs-map-{hash}.v1.json");
    }

    public static async Task<FavoriteFilesystemMapDocument?> TryLoadAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        await FileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var doc = await JsonSerializer.DeserializeAsync<FavoriteFilesystemMapDocument>(fs, JsonOptions, ct)
                .ConfigureAwait(false);
            if (doc?.Entries == null)
                return doc;
            var rebuilt = new Dictionary<string, FavoriteFilesystemMapEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in doc.Entries)
            {
                var k = FavoriteIndexRoots.NormalizeFavoritePath(kv.Key);
                rebuilt[k] = kv.Value;
            }

            doc.Entries = rebuilt;
            return doc;
        }
        catch
        {
            return null;
        }
        finally
        {
            FileGate.Release();
        }
    }

    /// <summary>Merges snapshot into the map for <paramref name="indexRoot"/> if <paramref name="snapshot.Path"/> is under that root.</summary>
    public static async Task TryUpsertSubtreeSnapshotAsync(
        string mapsDirectory,
        string indexRoot,
        FolderMetricsSnapshot snapshot,
        CancellationToken ct = default)
    {
        if (snapshot.ScanScope != FolderMetricsScanScope.FullSubtree)
            return;
        var root = FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        var path = FavoriteIndexRoots.NormalizeFavoritePath(snapshot.Path);
        if (!string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            && !FavoriteIndexRoots.IsStrictSubpath(path, root))
            return;

        var file = MapFilePathForIndexRoot(mapsDirectory, root);
        await FileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            FavoriteFilesystemMapDocument doc;
            if (File.Exists(file))
            {
                await using (var readFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    doc = await JsonSerializer.DeserializeAsync<FavoriteFilesystemMapDocument>(readFs, JsonOptions, ct)
                              .ConfigureAwait(false)
                          ?? new FavoriteFilesystemMapDocument { IndexRoot = root, FormatVersion = 1 };
                }
            }
            else
            {
                doc = new FavoriteFilesystemMapDocument { IndexRoot = root, FormatVersion = 1 };
            }

            doc.Entries ??= new Dictionary<string, FavoriteFilesystemMapEntry>(StringComparer.OrdinalIgnoreCase);

            doc.IndexRoot = root;
            doc.FormatVersion = 1;
            doc.SavedAtUtc = DateTimeOffset.UtcNow;
            doc.Entries[path] = new FavoriteFilesystemMapEntry
            {
                AggregateSizeBytes = snapshot.AggregateSizeBytes,
                TotalFileCount = snapshot.TotalFileCount,
                ImageFileCount = snapshot.ImageFileCount,
                FolderMtimeUtc = snapshot.FolderMtimeUtc,
            };

            var tmp = file + ".tmp";
            await using (var writeFs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(writeFs, doc, JsonOptions, ct).ConfigureAwait(false);
                await writeFs.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Copy(tmp, file, overwrite: true);
            try
            {
                File.Delete(tmp);
            }
            catch
            {
                // ignored
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            FileGate.Release();
        }
    }

    /// <summary>Removes map entries for paths under any of <paramref name="purgeFolderPrefixes"/> (normalized).</summary>
    public static async Task PurgePathsAsync(
        string mapsDirectory,
        IReadOnlyList<string> indexRoots,
        IEnumerable<string> purgeFolderPrefixes,
        CancellationToken ct = default)
    {
        var prefixes = purgeFolderPrefixes
            .Select(FavoriteIndexRoots.NormalizeFavoritePath)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (prefixes.Count == 0)
            return;

        foreach (var root in indexRoots)
        {
            var file = MapFilePathForIndexRoot(mapsDirectory, root);
            if (!File.Exists(file))
                continue;

            await FileGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                FavoriteFilesystemMapDocument? loaded;
                await using (var readFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    loaded = await JsonSerializer.DeserializeAsync<FavoriteFilesystemMapDocument>(readFs, JsonOptions, ct)
                        .ConfigureAwait(false);
                }

                if (loaded?.Entries == null || loaded.Entries.Count == 0)
                    continue;

                var doc = loaded;
                var removed = false;
                var keys = doc.Entries.Keys.ToList();
                foreach (var k in keys)
                {
                    var kn = FavoriteIndexRoots.NormalizeFavoritePath(k);
                    foreach (var pfx in prefixes)
                    {
                        if (string.Equals(kn, pfx, StringComparison.OrdinalIgnoreCase)
                            || FavoriteIndexRoots.IsStrictSubpath(kn, pfx))
                        {
                            doc.Entries.Remove(k);
                            removed = true;
                            break;
                        }
                    }
                }

                if (!removed)
                    continue;

                doc.SavedAtUtc = DateTimeOffset.UtcNow;
                var tmp = file + ".tmp";
                await using (var writeFs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(writeFs, doc, JsonOptions, ct).ConfigureAwait(false);
                    await writeFs.FlushAsync(ct).ConfigureAwait(false);
                }

                File.Copy(tmp, file, overwrite: true);
                try
                {
                    File.Delete(tmp);
                }
                catch
                {
                    // ignored
                }
            }
            catch
            {
                // ignored
            }
            finally
            {
                FileGate.Release();
            }
        }
    }

    public static void TryDeleteAllMaps(string mapsDirectory)
    {
        try
        {
            if (!Directory.Exists(mapsDirectory))
                return;
            foreach (var f in Directory.EnumerateFiles(mapsDirectory, "favorite-fs-map-*.v1.json"))
            {
                try
                {
                    File.Delete(f);
                }
                catch
                {
                    // ignored
                }
            }
        }
        catch
        {
            // ignored
        }
    }
}
