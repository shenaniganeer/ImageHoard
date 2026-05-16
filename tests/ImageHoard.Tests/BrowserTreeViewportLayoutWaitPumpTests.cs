using ImageHoard.Core.Browse;
using Xunit;

namespace ImageHoard.Tests;

/// <summary>
/// Smoke tests for <see cref="BrowserTreeViewportLayoutWaitPump"/> using a fake tree/layout adapter (no WinUI).
/// </summary>
public sealed class BrowserTreeViewportLayoutWaitPumpTests
{
    /// <summary>Fake adapter: scroll pass fails until the simulated container is realized after N layout ticks.</summary>
    private sealed class FakeTreeViewportAdapter
    {
        public int PrepareCount { get; private set; }
        public int TryScrollCount { get; private set; }
        public int WaitLayoutCount { get; private set; }
        public int FallbackCount { get; private set; }

        private int _layoutGeneration;
        private readonly int _realizeAfterLayoutTicks;

        public FakeTreeViewportAdapter(int realizeAfterLayoutTicks) =>
            _realizeAfterLayoutTicks = realizeAfterLayoutTicks;

        public void PrepareViewport() => PrepareCount++;

        public bool TryScrollPassOnce()
        {
            TryScrollCount++;
            return _layoutGeneration >= _realizeAfterLayoutTicks;
        }

        public Task WaitForLayoutOrTimeoutAsync(int _)
        {
            WaitLayoutCount++;
            Interlocked.Increment(ref _layoutGeneration);
            return Task.CompletedTask;
        }

        public Task ApplyExhaustionFallbackAsync()
        {
            FallbackCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteAsync_succeeds_after_container_realizes_on_second_layout_tick()
    {
        var fake = new FakeTreeViewportAdapter(realizeAfterLayoutTicks: 2);

        await BrowserTreeViewportLayoutWaitPump.ExecuteAsync(
            fake.PrepareViewport,
            fake.TryScrollPassOnce,
            fake.WaitForLayoutOrTimeoutAsync,
            fake.ApplyExhaustionFallbackAsync,
            maxWaitMs: 250,
            maxLayoutCycles: 8);

        Assert.Equal(1, fake.PrepareCount);
        Assert.Equal(3, fake.TryScrollCount);
        Assert.Equal(2, fake.WaitLayoutCount);
        Assert.Equal(0, fake.FallbackCount);
    }

    [Fact]
    public async Task ExecuteAsync_invokes_timeout_fallback_when_scroll_never_succeeds()
    {
        var fake = new FakeTreeViewportAdapter(realizeAfterLayoutTicks: 999);

        await BrowserTreeViewportLayoutWaitPump.ExecuteAsync(
            fake.PrepareViewport,
            fake.TryScrollPassOnce,
            fake.WaitForLayoutOrTimeoutAsync,
            fake.ApplyExhaustionFallbackAsync,
            maxWaitMs: 250,
            maxLayoutCycles: 8);

        Assert.Equal(1, fake.PrepareCount);
        Assert.Equal(8, fake.TryScrollCount);
        Assert.Equal(8, fake.WaitLayoutCount);
        Assert.Equal(1, fake.FallbackCount);
    }

    [Fact]
    public async Task ExecuteAsync_invokes_fallback_when_wall_clock_budget_is_zero_before_first_layout_wait()
    {
        var fake = new FakeTreeViewportAdapter(realizeAfterLayoutTicks: 999);

        await BrowserTreeViewportLayoutWaitPump.ExecuteAsync(
            fake.PrepareViewport,
            fake.TryScrollPassOnce,
            fake.WaitForLayoutOrTimeoutAsync,
            fake.ApplyExhaustionFallbackAsync,
            maxWaitMs: 0,
            maxLayoutCycles: 8);

        Assert.Equal(1, fake.PrepareCount);
        Assert.Equal(0, fake.TryScrollCount);
        Assert.Equal(0, fake.WaitLayoutCount);
        Assert.Equal(1, fake.FallbackCount);
    }

    [Fact]
    public async Task ExecuteAsync_succeeds_immediately_when_first_scroll_pass_succeeds()
    {
        var fake = new FakeTreeViewportAdapter(realizeAfterLayoutTicks: 0);

        await BrowserTreeViewportLayoutWaitPump.ExecuteAsync(
            fake.PrepareViewport,
            fake.TryScrollPassOnce,
            fake.WaitForLayoutOrTimeoutAsync,
            fake.ApplyExhaustionFallbackAsync,
            maxWaitMs: 250,
            maxLayoutCycles: 8);

        Assert.Equal(1, fake.PrepareCount);
        Assert.Equal(1, fake.TryScrollCount);
        Assert.Equal(0, fake.WaitLayoutCount);
        Assert.Equal(0, fake.FallbackCount);
    }
}
