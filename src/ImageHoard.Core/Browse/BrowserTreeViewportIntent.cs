namespace ImageHoard.Core.Browse;

/// <summary>Why the folder tree viewport is being adjusted (FR-BR-01 / FR-BR-04).</summary>
public enum BrowserTreeViewportReason
{
    ColdBootRestore,
    AfterWizardImageDeletes,
    AfterWizardNavigateToParent,
    AfterWizardUndo,
    FolderNavigation,
    SiblingFolderNavigation,
    FindHitFolder,
    FindHitFile,
    ImageStep,
    KeyboardMove,
    RootPopulate,
}

/// <summary>
/// Immutable intent consumed by the WinUI viewport pump. Pure data from <see cref="BrowserTreeViewportIntentResolver"/>.
/// </summary>
/// <param name="VerticalAlignmentRatio">0 = pin row to top of viewport; 0.5 = center (cold-boot file anchor). Other intents typically use 0.</param>
public readonly record struct BrowserTreeViewportIntent(
    BrowserTreeViewportReason Reason,
    string? PrimaryPath,
    string? SecondaryPath,
    double VerticalAlignmentRatio,
    bool PreferSelectionFirst);
