using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

/// <summary>Browse2 expansion cap (same limit as <see cref="BrowserTreeSnapshot.MaxExpandedFolderPaths"/>).</summary>
public sealed class ExpansionStateTests
{
    [Fact]
    public void TryExpand_beyond_max_evicts_oldest_while_keeping_newest()
    {
        var ex = new ExpansionState();
        var root = @"C:\r";
        for (var i = 0; i < BrowserTreeSnapshot.MaxExpandedFolderPaths + 3; i++)
            ex.TryExpand(Path.Combine(root, "p" + i));

        Assert.True(ex.ExpandedPaths.Count <= BrowserTreeSnapshot.MaxExpandedFolderPaths);
        Assert.True(ex.Contains(Path.Combine(root, "p" + (BrowserTreeSnapshot.MaxExpandedFolderPaths + 2))));
    }

    [Fact]
    public void Load_last_paths_win_when_over_cap()
    {
        var ex = new ExpansionState();
        var many = Enumerable.Range(0, BrowserTreeSnapshot.MaxExpandedFolderPaths + 5)
            .Select(i => $@"C:\r\z{i}")
            .ToList();
        ex.Load(many);
        Assert.Equal(BrowserTreeSnapshot.MaxExpandedFolderPaths, ex.ExpandedPaths.Count);
        Assert.True(ex.Contains($@"C:\r\z{BrowserTreeSnapshot.MaxExpandedFolderPaths + 4}"));
    }

    [Fact]
    public void TryExpand_same_path_twice_second_call_is_noop()
    {
        var ex = new ExpansionState();
        Assert.True(ex.TryExpand(@"C:\r\a"));
        Assert.False(ex.TryExpand(@"c:\r\a"));
    }
}
