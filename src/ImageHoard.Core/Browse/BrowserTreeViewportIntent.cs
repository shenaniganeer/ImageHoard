namespace ImageHoard.Core.Browse;

/// <summary>Why the folder tree viewport is being adjusted (FR-BR-01 / FR-BR-04).</summary>
/// <summary>How the viewport pump should make the target row visible.</summary>
public enum BrowserTreeViewportVisibility
{
    /// <summary>Bring the row into view using alignment / <c>ScrollIntoView</c> (default for Find, wizard, cold boot, etc.).</summary>
    BringIntoView,

    /// <summary>
    /// Do not scroll when the target row is fully inside the folder tree scroll viewport;
    /// otherwise jump by whole viewport heights until the row fits (image next/previous stepping).
    /// </summary>
    PageWhenOutsideViewport,
}

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
/// <param name="Visibility"><see cref="BrowserTreeViewportVisibility.BringIntoView"/> unless the intent opts into page-on-exit scrolling.</param>
public readonly record struct BrowserTreeViewportIntent(
    BrowserTreeViewportReason Reason,
    string? PrimaryPath,
    string? SecondaryPath,
    double VerticalAlignmentRatio,
    bool PreferSelectionFirst,
    BrowserTreeViewportVisibility Visibility = BrowserTreeViewportVisibility.BringIntoView);
