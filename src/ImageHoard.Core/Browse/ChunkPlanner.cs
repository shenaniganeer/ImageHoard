namespace ImageHoard.Core.Browse;

/// <summary>Deterministic index ranges for chunked UI population (browser tree staging).</summary>
public static class ChunkPlanner
{
    /// <summary>Returns (offset, length) for each chunk covering <paramref name="total"/> items.</summary>
    public static IEnumerable<(int Offset, int Length)> EnumerateChunks(int total, int chunkSize)
    {
        if (total <= 0 || chunkSize <= 0)
            yield break;

        for (var i = 0; i < total; i += chunkSize)
        {
            var len = Math.Min(chunkSize, total - i);
            yield return (i, len);
        }
    }
}
