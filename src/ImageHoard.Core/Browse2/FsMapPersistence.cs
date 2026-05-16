using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>Disk layout for <see cref="FsMapDocument"/> (separate file from legacy metrics-only v1 maps).</summary>
public static class FsMapPersistence
{
    private static readonly SemaphoreSlim FileGate = new(1, 1);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string MapFilePathForIndexRoot(string mapsDirectory, string indexRoot)
    {
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)));
        return Path.Combine(mapsDirectory, $"favorite-browse2-fs-{hash}.v1.json");
    }

    public static async Task<FsMapDocument?> TryLoadAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        await FileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var doc = await JsonSerializer.DeserializeAsync<FsMapDocument>(fs, JsonOptions, ct).ConfigureAwait(false);
            if (doc?.Entries == null)
                return doc;
            var rebuilt = new Dictionary<string, FsMapEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in doc.Entries)
            {
                var k = FavoriteIndexRoots.NormalizeFavoritePath(kv.Key);
                rebuilt[k] = kv.Value;
            }

            doc.Entries = rebuilt;
            doc.IndexRoot = FavoriteIndexRoots.NormalizeFavoritePath(doc.IndexRoot);
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

    public static async Task SaveAsync(string filePath, FsMapDocument doc, CancellationToken ct = default)
    {
        await FileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            doc.FormatVersion = 1;
            doc.SavedAtUtc = DateTimeOffset.UtcNow;
            var tmp = filePath + ".tmp";
            await using (var writeFs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(writeFs, doc, JsonOptions, ct).ConfigureAwait(false);
                await writeFs.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Copy(tmp, filePath, overwrite: true);
            try
            {
                File.Delete(tmp);
            }
            catch
            {
                // ignored
            }
        }
        finally
        {
            FileGate.Release();
        }
    }
}
