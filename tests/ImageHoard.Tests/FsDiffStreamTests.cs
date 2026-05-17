using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

public sealed class FsDiffStreamTests
{
    [Fact]
    public void Raise_invokes_subscribers_in_subscription_order()
    {
        var order = new List<int>();
        var stream = new FsDiffStream();
        stream.DiffReceived += _ => order.Add(1);
        stream.DiffReceived += _ => order.Add(2);
        stream.Raise(new FsAggregatesUpdatedDiff("R", "P", 1, 1, 1));
        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public void RaiseMany_delivers_each_diff_in_order()
    {
        var seen = new List<Type>();
        var stream = new FsDiffStream();
        stream.DiffReceived += d => seen.Add(d.GetType());
        stream.RaiseMany(
            new FsMapDiff[]
            {
                new FsFolderRemovedDiff("R", @"C:\a\x", @"C:\a"),
                new FsFolderAddedDiff(
                    "R",
                    @"C:\a\y",
                    @"C:\a",
                    new FsMapEntry { Name = "y", ParentPath = @"C:\a", HasSubfolders = false }),
            });
        Assert.Equal(typeof(FsFolderRemovedDiff), seen[0]);
        Assert.Equal(typeof(FsFolderAddedDiff), seen[1]);
    }

    [Fact]
    public void FsMapWorkspace_UpsertDirectoryRow_notifies_diff_stream()
    {
        var stream = new FsDiffStream();
        FsMapDiff? last = null;
        stream.DiffReceived += d => last = d;
        var ws = new FsMapWorkspace(@"C:\root", @"C:\Temp\m.json", stream);
        ws.UpsertDirectoryRow(@"C:\root", "", "root", null, false, 0, 0, 0, null);
        Assert.NotNull(last);
        Assert.IsType<FsFolderAddedDiff>(last);
    }
}
