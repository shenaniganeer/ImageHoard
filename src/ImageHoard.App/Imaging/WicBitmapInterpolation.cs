using Windows.Graphics.Imaging;

namespace ImageHoard.App.Imaging;

/// <summary>
/// Picks WIC interpolation by downscale ratio. Never uses <see cref="BitmapInterpolationMode.NearestNeighbor"/>.
/// </summary>
internal static class WicBitmapInterpolation
{
    /// <summary>
    /// <paramref name="maxLinearDownscaleRatio"/> is max(orientedW/outW, orientedH/outH).
    /// </summary>
    public static BitmapInterpolationMode ForDownscale(double maxLinearDownscaleRatio)
    {
        if (maxLinearDownscaleRatio <= 1.01)
            return BitmapInterpolationMode.Linear;
        if (maxLinearDownscaleRatio >= 4.0)
            return BitmapInterpolationMode.Fant;
        if (maxLinearDownscaleRatio >= 2.0)
            return BitmapInterpolationMode.Cubic;
        return BitmapInterpolationMode.Linear;
    }
}
