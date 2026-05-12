namespace ImageHoard.App;

/// <summary>
/// WinUI TreeViewNode has no Tag; path and label are stored in <see cref="Microsoft.UI.Xaml.Controls.TreeViewNode.Content"/>.
/// </summary>
internal sealed class FolderTreeEntry
{
    public required string Path { get; init; }
    public required string DisplayLabel { get; init; }

    public override string ToString() => DisplayLabel;
}
