using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

/// <summary>Covers <see cref="BrowserFindDeepFolderMapQuery"/> used by <c>BrowserFindController</c> for deep folder hits.</summary>
public sealed class BrowserFindMapQueryTests
{
    [Fact]
    public void Search_excludes_root_and_matches_name_contains()
    {
        var diff = new FsDiffStream();
        var ws = new FsMapWorkspace(@"C:\root", @"C:\t\m.json", diff);
        ws.UpsertDirectoryRow(@"C:\root", "", "root", null, true, 0, 0, 0, null);
        ws.UpsertDirectoryRow(@"C:\root\alpha", @"C:\root", "alpha", null, false, 0, 0, 0, null);
        ws.UpsertDirectoryRow(@"C:\root\beta", @"C:\root", "beta", null, false, 0, 0, 0, null);

        var hits = BrowserFindDeepFolderMapQuery.Search(ws, @"C:\root", "et", matchFromStartOfName: false);
        Assert.Single(hits);
        Assert.Equal(@"C:\root\beta", hits[0].Path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_matchFromStart_filters_prefix()
    {
        var diff = new FsDiffStream();
        var ws = new FsMapWorkspace(@"C:\root", @"C:\t\m.json", diff);
        ws.UpsertDirectoryRow(@"C:\root", "", "root", null, true, 0, 0, 0, null);
        ws.UpsertDirectoryRow(@"C:\root\pre", @"C:\root", "pre", null, false, 0, 0, 0, null);
        ws.UpsertDirectoryRow(@"C:\root\xpre", @"C:\root", "xpre", null, false, 0, 0, 0, null);

        var hits = BrowserFindDeepFolderMapQuery.Search(ws, @"C:\root", "pre", matchFromStartOfName: true);
        Assert.Single(hits);
        Assert.Equal(@"C:\root\pre", hits[0].Path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_null_workspace_returns_empty()
    {
        var hits = BrowserFindDeepFolderMapQuery.Search(null, @"C:\root", "x", matchFromStartOfName: false);
        Assert.Empty(hits);
    }
}
