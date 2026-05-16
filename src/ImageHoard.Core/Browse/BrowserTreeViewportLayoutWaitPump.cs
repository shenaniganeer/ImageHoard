namespace ImageHoard.Core.Browse;

/// <summary>
/// Layout-driven retry loop shared with the WinUI browser tree viewport pump (<c>MainWindow.BrowserViewport</c>).
/// Extracted for deterministic unit tests with fake scroll/layout adapters.
/// </summary>
public static class BrowserTreeViewportLayoutWaitPump
{
    /// <summary>
    /// Runs <paramref name="prepareViewport"/> once, then up to <paramref name="maxLayoutCycles"/> attempts of
    /// <paramref name="tryScrollPassOnce"/> separated by <paramref name="waitForLayoutOrTimeoutAsync"/> until success,
    /// the wall-clock budget expires, or cycles are exhausted; then invokes <paramref name="applyExhaustionFallbackAsync"/>.
    /// </summary>
    public static async Task ExecuteAsync(
        Action prepareViewport,
        Func<bool> tryScrollPassOnce,
        Func<int, Task> waitForLayoutOrTimeoutAsync,
        Func<Task> applyExhaustionFallbackAsync,
        int maxWaitMs,
        int maxLayoutCycles,
        CancellationToken cancellationToken = default)
    {
        prepareViewport();

        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
        for (var cycle = 0; cycle < maxLayoutCycles && DateTime.UtcNow < deadline; cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tryScrollPassOnce())
                return;

            var remainingMs = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
            await waitForLayoutOrTimeoutAsync(remainingMs).ConfigureAwait(true);
        }

        await applyExhaustionFallbackAsync().ConfigureAwait(true);
    }
}
