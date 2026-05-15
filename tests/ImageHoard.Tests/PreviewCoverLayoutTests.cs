using ImageHoard.Core.Imaging;
using Xunit;

namespace ImageHoard.Tests;

public sealed class PreviewCoverLayoutTests
{
    [Fact]
    public void CoverWouldRequireUpscale_False_WhenCoverScaleIsDownscaleOnly()
    {
        Assert.False(PreviewCoverLayout.CoverWouldRequireUpscale(400, 300, 800, 600));
    }

    [Fact]
    public void CoverWouldRequireUpscale_True_WhenImageSmallerThanViewportBothAxes()
    {
        Assert.True(PreviewCoverLayout.CoverWouldRequireUpscale(400, 300, 200, 150));
    }

    [Fact]
    public void CoverWouldRequireUpscale_False_WhenDimensionsInvalid()
    {
        Assert.False(PreviewCoverLayout.CoverWouldRequireUpscale(0, 300, 200, 150));
        Assert.False(PreviewCoverLayout.CoverWouldRequireUpscale(400, 300, 0, 150));
    }

    [Fact]
    public void UniformContainScale_TallImage_LimitedByHeight()
    {
        // Viewport wide; tall image — height ratio is smaller.
        Assert.Equal(500.0 / 800.0, PreviewCoverLayout.UniformContainScale(1000, 500, 400, 800), 5);
    }

    [Fact]
    public void UniformContainScale_WideImage_LimitedByWidth()
    {
        Assert.Equal(600.0 / 800.0, PreviewCoverLayout.UniformContainScale(600, 1000, 800, 400), 5);
    }

    [Fact]
    public void UniformContainScale_ReturnsZero_WhenDimensionsInvalid()
    {
        Assert.Equal(0, PreviewCoverLayout.UniformContainScale(0, 100, 50, 50));
        Assert.Equal(0, PreviewCoverLayout.UniformContainScale(100, 100, 0, 50));
    }

    [Fact]
    public void UniformContainScaleShrinkOnly_CapsAtOne_WhenImageSmallerThanViewport()
    {
        Assert.Equal(1.0, PreviewCoverLayout.UniformContainScaleShrinkOnly(800, 600, 400, 300));
    }

    [Fact]
    public void UniformContainScaleShrinkOnly_MatchesContain_WhenDownscale()
    {
        var contain = PreviewCoverLayout.UniformContainScale(400, 300, 800, 800);
        Assert.Equal(contain, PreviewCoverLayout.UniformContainScaleShrinkOnly(400, 300, 800, 800));
    }
}
