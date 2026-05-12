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
}
