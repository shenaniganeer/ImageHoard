using System.IO;
using ImageHoard.Core.Browse;
using Xunit;

namespace ImageHoard.Tests;

public sealed class BrowserTreeViewportBrowse2FolderTargetTests
{
    [Fact]
    public void Resolve_ColdBoot_returns_null()
    {
        var state = new BrowserPaneState("C:\\a", null, null, null, null);
        var intent = new BrowserTreeViewportIntent(
            BrowserTreeViewportReason.ColdBootRestore,
            "C:\\a",
            null,
            0,
            false);
        Assert.Null(BrowserTreeViewportBrowse2FolderTarget.ResolveFolderPathForViewportScroll(intent, state));
    }

    [Fact]
    public void Resolve_ImageStep_uses_secondary_then_pin()
    {
        var state = new BrowserPaneState("C:\\root", null, null, null, null);
        var img = Path.Combine("C:\\root", "sub", "x.jpg");
        var parent = Path.Combine("C:\\root", "sub");
        var intent = BrowserTreeViewportIntentResolver.ForImageStep(state, img);
        var folder = BrowserTreeViewportBrowse2FolderTarget.ResolveFolderPathForViewportScroll(intent, state);
        Assert.Equal(Path.GetFullPath(parent), Path.GetFullPath(folder!));
    }

    [Fact]
    public void Resolve_SiblingFolder_uses_primary()
    {
        var state = new BrowserPaneState("C:\\root", null, null, null, null);
        var target = Path.Combine("C:\\root", "other");
        var intent = BrowserTreeViewportIntentResolver.ForSiblingFolderNav(state, target);
        var folder = BrowserTreeViewportBrowse2FolderTarget.ResolveFolderPathForViewportScroll(intent, state);
        Assert.Equal(Path.GetFullPath(target), Path.GetFullPath(folder!));
    }
}
