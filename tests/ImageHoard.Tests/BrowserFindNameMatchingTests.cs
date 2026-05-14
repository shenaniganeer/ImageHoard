using ImageHoard.Core.Browse;

namespace ImageHoard.Tests;

public sealed class BrowserFindNameMatchingTests
{
    [Theory]
    [InlineData("foo", "foobar", true, true)]
    [InlineData("foo", "barfoo", true, false)]
    [InlineData("foo", "barfoo", false, true)]
    [InlineData("FOO", "foobar", true, true)]
    [InlineData("  bar  ", "xbarz", false, true)]
    [InlineData("", "name", true, false)]
    [InlineData("   ", "name", false, false)]
    public void NameMatches_expected(string query, string name, bool fromStart, bool expected) =>
        Assert.Equal(expected, BrowserFindNameMatching.NameMatches(query, name, fromStart));
}
