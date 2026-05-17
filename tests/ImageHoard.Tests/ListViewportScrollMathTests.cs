using ImageHoard.Core.Browse2;
using Xunit;

namespace ImageHoard.Tests;

public sealed class ListViewportScrollMathTests
{
    [Fact]
    public void TryCompute_skip_when_fully_visible()
    {
        var r = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemContentTop: 100,
            itemContentHeight: 32,
            viewportOffset: 80,
            viewportHeight: 200,
            maxScrollOffset: 500,
            pinItemTopAtViewportY: 64,
            skipIfFullyVisible: true);
        Assert.Null(r);
    }

    [Fact]
    public void TryCompute_third_row_pin_fixed_height()
    {
        var r = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemContentTop: 320,
            itemContentHeight: 32,
            viewportOffset: 0,
            viewportHeight: 200,
            maxScrollOffset: 1000,
            pinItemTopAtViewportY: 64,
            skipIfFullyVisible: true);
        Assert.Equal(256, r);
    }

    [Fact]
    public void TryCompute_center_pin_variable_height()
    {
        var itemH = 48.0;
        var vh = 200.0;
        var pin = (vh - itemH) / 2;
        var r = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemContentTop: 400,
            itemContentHeight: itemH,
            viewportOffset: 0,
            viewportHeight: vh,
            maxScrollOffset: 800,
            pinItemTopAtViewportY: pin,
            skipIfFullyVisible: true);
        Assert.Equal(400 - pin, r);
    }

    [Fact]
    public void TryCompute_clamps_to_zero()
    {
        var r = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemContentTop: 40,
            itemContentHeight: 32,
            viewportOffset: 200,
            viewportHeight: 100,
            maxScrollOffset: 500,
            pinItemTopAtViewportY: 64,
            skipIfFullyVisible: true);
        Assert.Equal(0, r);
    }

    [Fact]
    public void TryCompute_clamps_to_max()
    {
        var r = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemContentTop: 900,
            itemContentHeight: 32,
            viewportOffset: 0,
            viewportHeight: 100,
            maxScrollOffset: 50,
            pinItemTopAtViewportY: 64,
            skipIfFullyVisible: true);
        Assert.Equal(50, r);
    }

    [Fact]
    public void TryCompute_null_when_item_taller_than_viewport()
    {
        var r = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemContentTop: 0,
            itemContentHeight: 150,
            viewportOffset: 0,
            viewportHeight: 100,
            maxScrollOffset: 500,
            pinItemTopAtViewportY: 0,
            skipIfFullyVisible: false);
        Assert.Null(r);
    }

    [Fact]
    public void TryCompute_null_when_no_effective_change()
    {
        var r = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemContentTop: 200,
            itemContentHeight: 32,
            viewportOffset: 136,
            viewportHeight: 200,
            maxScrollOffset: 500,
            pinItemTopAtViewportY: 64,
            skipIfFullyVisible: true);
        Assert.Null(r);
    }
}
