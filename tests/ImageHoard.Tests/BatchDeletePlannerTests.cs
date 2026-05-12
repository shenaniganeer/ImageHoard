using ImageHoard.Core.Sort;

namespace ImageHoard.Tests;

public sealed class BatchDeletePlannerTests
{
    [Fact]
    public void TryGetDeletionSet_blocks_when_unset_exists()
    {
        var paths = new[] { "a.jpg", "b.jpg", "c.jpg" };
        var session = new SortSession();
        session.SetState("a.jpg", SortFlagState.Keep);
        session.SetState("b.jpg", SortFlagState.Delete);
        // c.jpg unset

        var ok = BatchDeletePlanner.TryGetDeletionSet(paths, session, out var del, out var reason);
        Assert.False(ok);
        Assert.Null(del);
        Assert.Equal("unset", reason);
    }

    [Fact]
    public void TryGetDeletionSet_deletes_non_keep_when_all_decided()
    {
        var paths = new[] { "a.jpg", "b.jpg" };
        var session = new SortSession();
        session.SetState("a.jpg", SortFlagState.Keep);
        session.SetState("b.jpg", SortFlagState.Delete);

        var ok = BatchDeletePlanner.TryGetDeletionSet(paths, session, out var del, out _);
        Assert.True(ok);
        Assert.NotNull(del);
        Assert.Single(del!);
        Assert.Equal("b.jpg", del[0]);
    }

    [Fact]
    public void TryGetDeletionSet_delete_flag_still_deleted_at_commit()
    {
        var paths = new[] { "a.jpg", "b.jpg" };
        var session = new SortSession();
        session.SetState("a.jpg", SortFlagState.Keep);
        session.SetState("b.jpg", SortFlagState.Delete);

        BatchDeletePlanner.TryGetDeletionSet(paths, session, out var del, out _);
        Assert.Contains("b.jpg", del!);
    }
}
