namespace ImageHoard.Core.Browse;

/// <summary>
/// FIFO queue of preview paths with optional lag-based coalescing for rapid navigation.
/// </summary>
public sealed class PreviewNavigationQueue
{
    private readonly List<(string Path, long Ticks)> _items = new();

    /// <summary>Number of pending paths (after last dequeue or coalesce).</summary>
    public int Count => _items.Count;

    public void Clear() => _items.Clear();

    /// <summary>
    /// Adds a path unless it equals the last enqueued path (ordinal case-insensitive), in which case no-op.
    /// </summary>
    /// <returns>False when deduplicated and nothing was added.</returns>
    public bool TryEnqueue(string path, long enqueueTicks)
    {
        if (_items.Count > 0
            && string.Equals(_items[^1].Path, path, StringComparison.OrdinalIgnoreCase))
            return false;

        _items.Add((path, enqueueTicks));
        return true;
    }

    /// <summary>
    /// If there are at least two pending items and the oldest has waited long enough per <paramref name="catchUpLagSeconds"/>,
    /// drops every item except the newest. When <paramref name="catchUpLagSeconds"/> is &lt;= 0, coalescing is disabled (FIFO until decoded).
    /// </summary>
    /// <returns>True when items were removed.</returns>
    public bool TryCoalesceIfBehind(double catchUpLagSeconds, long nowTicks, long stopwatchFrequency)
    {
        if (_items.Count < 2)
            return false;

        var oldestAgeSec = TicksToSeconds(nowTicks - _items[0].Ticks, stopwatchFrequency);
        if (!ShouldCoalesceForCatchUp(catchUpLagSeconds, oldestAgeSec))
            return false;

        var latest = _items[^1];
        _items.Clear();
        _items.Add(latest);
        return true;
    }

    public bool TryDequeue(out string path)
    {
        if (_items.Count == 0)
        {
            path = "";
            return false;
        }

        (path, _) = _items[0];
        _items.RemoveAt(0);
        return true;
    }

    /// <summary>Seconds since the oldest item was enqueued, or 0 if empty.</summary>
    public double PeekOldestAgeSeconds(long nowTicks, long stopwatchFrequency)
    {
        if (_items.Count == 0)
            return 0;
        return TicksToSeconds(nowTicks - _items[0].Ticks, stopwatchFrequency);
    }

    public static double TicksToSeconds(long deltaTicks, long stopwatchFrequency) =>
        stopwatchFrequency > 0 ? deltaTicks / (double)stopwatchFrequency : 0;

    public static bool ShouldCoalesceForCatchUp(double catchUpLagSeconds, double oldestAgeSeconds)
    {
        if (catchUpLagSeconds <= 0)
            return false;
        return oldestAgeSeconds >= catchUpLagSeconds;
    }
}
