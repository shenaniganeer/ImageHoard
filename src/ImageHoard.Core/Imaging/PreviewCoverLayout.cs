namespace ImageHoard.Core.Imaging;

/// <summary>
/// Helpers for preview/fit layout when mapping decoded bitmap DIPs into a viewport.
/// </summary>
public static class PreviewCoverLayout
{
    /// <summary>
    /// True when scaling the image to <b>cover</b> the viewport would require a scale factor greater than 1
    /// (compositor upscale relative to decoded DIP size).
    /// </summary>
    public static bool CoverWouldRequireUpscale(double viewportDipW, double viewportDipH, double imageDipW, double imageDipH)
    {
        if (viewportDipW <= 0 || viewportDipH <= 0 || imageDipW <= 0 || imageDipH <= 0)
            return false;
        return Math.Max(viewportDipW / imageDipW, viewportDipH / imageDipH) > 1.0;
    }

    /// <summary>
    /// Uniform scale to <b>contain</b> the image inside the viewport (full image visible, letterboxing as needed).
    /// Returns 0 when any dimension is non-positive.
    /// </summary>
    public static double UniformContainScale(double viewportDipW, double viewportDipH, double imageDipW, double imageDipH)
    {
        if (viewportDipW <= 0 || viewportDipH <= 0 || imageDipW <= 0 || imageDipH <= 0)
            return 0;
        return Math.Min(viewportDipW / imageDipW, viewportDipH / imageDipH);
    }

    /// <summary>
    /// Same as <see cref="UniformContainScale"/> but never greater than 1 (no compositor upscale).
    /// </summary>
    public static double UniformContainScaleShrinkOnly(double viewportDipW, double viewportDipH, double imageDipW, double imageDipH)
    {
        var s = UniformContainScale(viewportDipW, viewportDipH, imageDipW, imageDipH);
        return s <= 0 ? 0 : Math.Min(1.0, s);
    }
}
