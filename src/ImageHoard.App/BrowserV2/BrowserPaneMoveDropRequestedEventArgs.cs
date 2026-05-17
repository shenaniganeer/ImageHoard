namespace ImageHoard.App.BrowserV2;

/// <summary>Raised when the user drops internal path drag data onto a valid folder target.</summary>
public sealed class BrowserPaneMoveDropRequestedEventArgs : EventArgs
{
    public required IReadOnlyList<string> SourcePaths { get; init; }

    public required string DestinationDirectory { get; init; }
}
