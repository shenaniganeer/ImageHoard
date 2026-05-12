using ImageHoard.Core.Imaging;
using Xunit;

namespace ImageHoard.Tests;

public sealed class BitmapDecodeFitTests
{
    [Fact]
    public void Fit_UniformScalesDownToBox()
    {
        var (w, h) = BitmapDecodeFit.ComputeOutputDimensions(2000, 1000, 400, 300, 8192, BitmapUniformKind.Fit);
        Assert.Equal(400u, w);
        Assert.Equal(200u, h);
    }

    [Fact]
    public void Fit_DoesNotUpscale()
    {
        var (w, h) = BitmapDecodeFit.ComputeOutputDimensions(400, 300, 2000, 1500, 8192, BitmapUniformKind.Fit);
        Assert.Equal(400u, w);
        Assert.Equal(300u, h);
    }

    [Fact]
    public void Fill_UsesMaxAxisRatio()
    {
        var (w, h) = BitmapDecodeFit.ComputeOutputDimensions(2000, 1000, 400, 300, 8192, BitmapUniformKind.Fill);
        Assert.Equal(600u, w);
        Assert.Equal(300u, h);
    }

    [Fact]
    public void FullResolution_AppliesCapOnly()
    {
        var (w, h) = BitmapDecodeFit.ComputeOutputDimensions(10000, 5000, 100, 100, 8192, BitmapUniformKind.FullResolution);
        Assert.True(w <= 8192u);
        Assert.True(h <= 8192u);
        Assert.Equal(8192u, w);
        Assert.Equal(4096u, h);
    }

    [Fact]
    public void MaxLinearDownscaleRatio_IsAtLeastOne()
    {
        var r = BitmapDecodeFit.MaxLinearDownscaleRatio(2000, 1000, 400, 200);
        Assert.InRange(r, 5.0, 5.01);
    }
}
