using System.Text.RegularExpressions;
using Xunit;

namespace ImageHoard.Tests;

/// <summary>
/// Regression guard for tree slideshow + Browse2: per-slide preview commits must not call
/// <c>SyncTreeSelectionToImagePath</c> while <c>_slideshowUiActive</c> (that would rebind
/// <c>ImagePaneController.CurrentFolderPath</c> via <c>TrySyncBrowseTreeSelectionToImagePathAsync</c>).
/// FR-SL-06 / FR-SL-07 browse handoff stays on explicit <c>slideshow.switchToBrowseAtCurrentLocation</c>.
/// </summary>
public sealed class SlideshowBrowse2PreviewNavigationContractTests
{
    private static string? TryFindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var sln = Path.Combine(d.FullName, "ImageHoard.sln");
            if (File.Exists(sln))
                return d.FullName;
        }

        return null;
    }

    [Fact]
    public void MainWindow_PreviewNavigation_DoesNotSyncBrowseTreeOnEachSlideshowSlide()
    {
        var root = TryFindRepoRoot();
        Assert.NotNull(root);

        var path = Path.Combine(
            root,
            "src",
            "ImageHoard.App",
            "MainWindow.PreviewNavigation.cs");
        Assert.True(File.Exists(path), $"Expected {path} under repo root.");

        var text = File.ReadAllText(path);

        // Must keep browse coalesce re-anchor after drain.
        Assert.Contains("if (!_slideshowUiActive && didCoalesce)", text, StringComparison.Ordinal);
        Assert.Contains("SyncTreeSelectionToImagePath(path);", text, StringComparison.Ordinal);

        // Forbidden: slideshow-only per-frame sync into Browse2 (see browser-navigation-wizard-tree-coordination.md).
        var forbidden = new Regex(
            @"if\s*\(\s*_slideshowUiActive\s*\)\s*\r?\n\s*SyncTreeSelectionToImagePath\s*\(",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.False(
            forbidden.IsMatch(text),
            "DecodeAndCommitPreviewAsync must not call SyncTreeSelectionToImagePath while _slideshowUiActive; " +
            "use slideshow.switchToBrowseAtCurrentLocation to align Browse2 with the slide.");
    }
}
