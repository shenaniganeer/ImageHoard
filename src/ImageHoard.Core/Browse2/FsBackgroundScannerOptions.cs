namespace ImageHoard.Core.Browse2;

public sealed class FsBackgroundScannerOptions
{
    /// <summary>Cooperative yield frequency while walking directories (0 disables explicit yields).</summary>
    public int YieldEveryNDirectories { get; set; } = 32;

    public Action<string>? DirectoryVisited { get; set; }
}
