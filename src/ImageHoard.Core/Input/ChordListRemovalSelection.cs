namespace ImageHoard.Core.Input;

/// <summary>
/// Maps a text selection or caret in a chord list display string to variant indices to remove.
/// Ranges are half-open <c>[Start, EndExclusive)</c> per variant; gaps may appear between variants (separators).
/// </summary>
public static class ChordListRemovalSelection
{
    /// <summary>
    /// Returns distinct variant indices to remove, sorted descending so callers can <c>RemoveAt</c> safely.
    /// </summary>
    public static List<int> GetVariantIndicesToRemove(
        IReadOnlyList<(int Start, int EndExclusive)> ranges,
        int displayLength,
        int selectionStart,
        int selectionLength,
        bool isBackspace)
    {
        if (ranges.Count == 0)
            return new List<int>();

        var selStart = Clamp(selectionStart, 0, displayLength);
        var len = Math.Max(0, selectionLength);
        if (len > 0)
        {
            var a = selStart;
            var b = selStart + len;
            var set = new HashSet<int>();
            for (var i = 0; i < ranges.Count; i++)
            {
                var (s, e) = ranges[i];
                if (e > a && s < b)
                    set.Add(i);
            }

            return set.OrderByDescending(i => i).ToList();
        }

        var caret = selStart;
        if (isBackspace)
        {
            if (caret == 0)
                return new List<int>();
            var t = caret - 1;
            var idx = FindVariantContaining(ranges, t);
            if (idx >= 0)
                return new List<int> { idx };
            var left = FindGapLeftVariantIndex(ranges, t);
            return left >= 0 ? new List<int> { left } : new List<int>();
        }

        if (caret >= displayLength)
            return new List<int> { ranges.Count - 1 };
        var delIdx = FindVariantContaining(ranges, caret);
        if (delIdx >= 0)
            return new List<int> { delIdx };
        var right = FindGapRightVariantIndex(ranges, caret);
        return right >= 0 ? new List<int> { right } : new List<int>();
    }

    private static int FindVariantContaining(IReadOnlyList<(int Start, int EndExclusive)> ranges, int index)
    {
        for (var i = 0; i < ranges.Count; i++)
        {
            var (s, e) = ranges[i];
            if (s <= index && index < e)
                return i;
        }

        return -1;
    }

    /// <summary>When <paramref name="index"/> lies in a separator gap, return the variant to the left (Backspace).</summary>
    private static int FindGapLeftVariantIndex(IReadOnlyList<(int Start, int EndExclusive)> ranges, int index)
    {
        for (var i = 0; i < ranges.Count - 1; i++)
        {
            var endLeft = ranges[i].EndExclusive;
            var startRight = ranges[i + 1].Start;
            if (endLeft <= index && index < startRight)
                return i;
        }

        return -1;
    }

    /// <summary>When <paramref name="index"/> lies in a separator gap, return the variant to the right (Delete).</summary>
    private static int FindGapRightVariantIndex(IReadOnlyList<(int Start, int EndExclusive)> ranges, int index)
    {
        for (var i = 0; i < ranges.Count - 1; i++)
        {
            var endLeft = ranges[i].EndExclusive;
            var startRight = ranges[i + 1].Start;
            if (endLeft <= index && index < startRight)
                return i + 1;
        }

        return -1;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}
