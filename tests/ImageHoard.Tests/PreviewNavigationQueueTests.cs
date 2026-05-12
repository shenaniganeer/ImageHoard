using ImageHoard.Core.Browse;

namespace ImageHoard.Tests;

public sealed class PreviewNavigationQueueTests
{
    private const long Freq = 10_000_000; // pretend 10M ticks = 1 second

    [Fact]
    public void Enqueue_DedupesAdjacentSamePathCaseInsensitive()
    {
        var q = new PreviewNavigationQueue();
        Assert.True(q.TryEnqueue(@"C:\a.png", 0));
        Assert.False(q.TryEnqueue(@"C:\A.PNG", 1));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Fifo_WhenNotBehind_DoesNotCoalesce()
    {
        var q = new PreviewNavigationQueue();
        Assert.True(q.TryEnqueue("a", 0));
        Assert.True(q.TryEnqueue("b", 0));
        Assert.False(q.TryCoalesceIfBehind(0.5, nowTicks: 1 * Freq / 10, Freq)); // 0.1s — not >= 0.5
        Assert.True(q.TryDequeue(out var p));
        Assert.Equal("a", p);
        Assert.True(q.TryDequeue(out p));
        Assert.Equal("b", p);
        Assert.False(q.TryDequeue(out _));
    }

    [Fact]
    public void Coalesce_WhenOldestExceedsLag_KeepsOnlyLatest()
    {
        var q = new PreviewNavigationQueue();
        Assert.True(q.TryEnqueue("a", 0));
        Assert.True(q.TryEnqueue("b", 0));
        Assert.True(q.TryEnqueue("c", 0));
        Assert.True(q.TryCoalesceIfBehind(0.5, nowTicks: (long)(0.6 * Freq), Freq));
        Assert.Equal(1, q.Count);
        Assert.True(q.TryDequeue(out var p));
        Assert.Equal("c", p);
    }

    [Fact]
    public void Coalesce_WhenLagIsZero_DoesNotCoalesce()
    {
        var q = new PreviewNavigationQueue();
        Assert.True(q.TryEnqueue("a", 0));
        Assert.True(q.TryEnqueue("b", 0));
        Assert.False(q.TryCoalesceIfBehind(0, nowTicks: 0, Freq));
        Assert.True(q.TryDequeue(out var p));
        Assert.Equal("a", p);
        Assert.True(q.TryDequeue(out p));
        Assert.Equal("b", p);
    }

    [Fact]
    public void PeekOldestAgeSeconds_UsesFirstItem()
    {
        var q = new PreviewNavigationQueue();
        Assert.True(q.TryEnqueue("a", 0));
        Assert.True(q.TryEnqueue("b", Freq));
        Assert.Equal(0.5, q.PeekOldestAgeSeconds(nowTicks: (long)(0.5 * Freq), Freq), precision: 5);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 0.1, false)]
    [InlineData(0.5, 0.49, false)]
    [InlineData(0.5, 0.5, true)]
    public void ShouldCoalesceForCatchUp_MatchesPolicy(double lag, double oldestAge, bool expected) =>
        Assert.Equal(expected, PreviewNavigationQueue.ShouldCoalesceForCatchUp(lag, oldestAge));
}
