namespace ImageHoard.Core.Io;

/// <summary>NFR-PF-06 — link cancellation with timeout for NAS I/O.</summary>
public static class IoTimeouts
{
    public static CancellationTokenSource CreateLinkedTimeout(
        CancellationToken parent,
        TimeSpan timeout,
        out CancellationToken linked)
    {
        var timeoutCts = new CancellationTokenSource(timeout);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parent, timeoutCts.Token);
        linked = linkedCts.Token;
        return linkedCts;
    }
}
