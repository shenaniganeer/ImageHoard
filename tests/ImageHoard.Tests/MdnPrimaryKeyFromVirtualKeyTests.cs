using ImageHoard.Core.Input;

namespace ImageHoard.Tests;

public sealed class MdnPrimaryKeyFromVirtualKeyTests
{
    [Theory]
    [InlineData(0xBB, "Equal")]
    [InlineData(0xBD, "Minus")]
    [InlineData(0x6B, "NumpadAdd")]
    [InlineData(0x6D, "NumpadSubtract")]
    public void TryOemAndNumpad_maps_expected_tokens(int vk, string expected)
    {
        Assert.Equal(expected, MdnPrimaryKeyFromVirtualKey.TryOemAndNumpad(vk));
    }

    [Fact]
    public void TryOemAndNumpad_unknown_returns_null()
    {
        Assert.Null(MdnPrimaryKeyFromVirtualKey.TryOemAndNumpad(0x41));
    }
}
