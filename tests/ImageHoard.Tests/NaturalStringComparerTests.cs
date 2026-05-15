using ImageHoard.Core;

namespace ImageHoard.Tests;

public sealed class NaturalStringComparerTests
{
    private static readonly NaturalStringComparer Comparer = NaturalStringComparer.OrdinalIgnoreCase;

    [Fact]
    public void Sorts_digit_suffixes_numerically_not_lexically()
    {
        var input = new[] { "shot_10.png", "shot_2.png", "shot_1.png", "shot_9.png" };
        var sorted = input.OrderBy(s => s, Comparer).ToArray();
        Assert.Equal(new[] { "shot_1.png", "shot_2.png", "shot_9.png", "shot_10.png" }, sorted);
    }

    [Fact]
    public void Case_insensitive_on_text_runs()
    {
        var input = new[] { "b_1.jpg", "A_2.jpg", "a_1.jpg" };
        var sorted = input.OrderBy(s => s, Comparer).ToArray();
        Assert.Equal(new[] { "a_1.jpg", "A_2.jpg", "b_1.jpg" }, sorted);
    }

    [Fact]
    public void Equal_numeric_digit_runs_break_tie_by_ordinal_digit_string()
    {
        var input = new[] { "x_01.jpg", "x_1.jpg" };
        var sorted = input.OrderBy(s => s, Comparer).ToArray();
        Assert.Equal(new[] { "x_01.jpg", "x_1.jpg" }, sorted);
    }

    [Fact]
    public void Compare_is_consistent_with_sort_order()
    {
        var a = "z_9";
        var b = "z_10";
        Assert.True(Comparer.Compare(a, b) < 0);
        Assert.True(Comparer.Compare(b, a) > 0);
        Assert.Equal(0, Comparer.Compare(a, a));
    }

    [Fact]
    public void Non_letters_sort_before_letters_in_text_runs()
    {
        Assert.True(Comparer.Compare("_A", "A") < 0);
        Assert.True(Comparer.Compare("_A", "a") < 0);
        Assert.True(Comparer.Compare("_b", "a") < 0);
        var sorted = new[] { "A", "_A", "a" }.OrderBy(s => s, Comparer).ToArray();
        Assert.Equal("_A", sorted[0]);
        Assert.Equal(new[] { "A", "a" }, sorted.Skip(1).OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Symbols_sort_before_digits_at_mixed_boundary()
    {
        Assert.True(Comparer.Compare("_1", "1") < 0);
        Assert.True(Comparer.Compare("1", "_1") > 0);
        Assert.True(Comparer.Compare(".9", "9") < 0);
        var ordered = new[] { "9", "_9", "a9" }.OrderBy(s => s, Comparer).ToArray();
        Assert.Equal(new[] { "_9", "9", "a9" }, ordered);
    }
}
