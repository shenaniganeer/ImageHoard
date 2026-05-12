namespace ImageHoard.Core;

/// <summary>
/// Ordinal-ignore-case string compare with ASCII digit runs ordered by numeric value (natural / "version" sort).
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static NaturalStringComparer OrdinalIgnoreCase { get; } = new();

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        var a = x.AsSpan();
        var b = y.AsSpan();
        var i = 0;
        var j = 0;
        while (i < a.Length && j < b.Length)
        {
            var da = IsAsciiDigit(a[i]);
            var db = IsAsciiDigit(b[j]);
            if (da && db)
            {
                var ca = i;
                var cb = j;
                while (i < a.Length && IsAsciiDigit(a[i]))
                    i++;
                while (j < b.Length && IsAsciiDigit(b[j]))
                    j++;
                var sa = a.Slice(ca, i - ca);
                var sb = b.Slice(cb, j - cb);
                if (TryParseAsciiULong(sa, out var na) && TryParseAsciiULong(sb, out var nb))
                {
                    if (na != nb)
                        return na < nb ? -1 : 1;
                    var tie = sa.CompareTo(sb, StringComparison.Ordinal);
                    if (tie != 0)
                        return tie;
                }
                else
                {
                    var c = sa.CompareTo(sb, StringComparison.OrdinalIgnoreCase);
                    if (c != 0)
                        return c;
                }
            }
            else if (!da && !db)
            {
                var ca = i;
                var cb = j;
                while (i < a.Length && !IsAsciiDigit(a[i]))
                    i++;
                while (j < b.Length && !IsAsciiDigit(b[j]))
                    j++;
                var c = a.Slice(ca, i - ca).CompareTo(b.Slice(cb, j - cb), StringComparison.OrdinalIgnoreCase);
                if (c != 0)
                    return c;
            }
            else
            {
                var ac = char.ToUpperInvariant(a[i]);
                var bc = char.ToUpperInvariant(b[j]);
                if (ac != bc)
                    return ac < bc ? -1 : 1;
                i++;
                j++;
            }
        }

        return a.Length - b.Length;
    }

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    /// <summary>Up to 19 decimal digits fit in <see cref="ulong"/>; longer runs fall back to lexicographic compare elsewhere.</summary>
    private static bool TryParseAsciiULong(ReadOnlySpan<char> digits, out ulong value)
    {
        value = 0;
        if (digits.IsEmpty)
            return true;
        if (digits.Length > 19)
            return false;
        foreach (var c in digits)
        {
            if (!IsAsciiDigit(c))
                return false;
            var d = (ulong)(c - '0');
            if (value > ulong.MaxValue / 10 || value * 10 > ulong.MaxValue - d)
                return false;
            value = value * 10 + d;
        }

        return true;
    }
}
