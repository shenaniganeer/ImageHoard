using System.IO;

namespace ImageHoard.App;

internal static class AppDataPaths
{
    /// <summary>Resolves FR-ST-01 data root (standard LocalAppData or portable ImageHoardData).</summary>
    public static string GetDataRoot()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        if (string.Equals(Environment.GetEnvironmentVariable("IMAGEHOARD_PORTABLE"), "1", StringComparison.Ordinal)
            || File.Exists(Path.Combine(exeDir, "ImageHoard.portable")))
            return Path.Combine(exeDir, "ImageHoardData");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageHoard");
    }

    public static string SettingsFilePath => Path.Combine(GetDataRoot(), "settings.json");

    public static string LogsDirectory => Path.Combine(GetDataRoot(), "logs");

    public static string OperationsLogPath => Path.Combine(LogsDirectory, "operations.jsonl");

    public static string CacheDirectory => Path.Combine(GetDataRoot(), "cache");

    public static string FolderMetricsCachePath => Path.Combine(CacheDirectory, "folder-metrics.jsonl");

    /// <summary>Persisted per-index-root subtree metrics map (deduped nested favorites share one file).</summary>
    public static string FavoriteFilesystemMapsDirectory => Path.Combine(CacheDirectory, "favorite-fs-maps");

    public static string UserInputOverridesPath => Path.Combine(GetDataRoot(), "user-input-overrides.v1.json");
}
