namespace ImageHoard.Core.Imaging;

/// <summary>
/// Pure math for decode output dimensions: one WIC scale stage, aligned with Uniform / UniformToFill / 1:1 display.
/// </summary>
public static class BitmapDecodeFit
{
    /// <summary>
    /// Maximum linear downscale ratio after fitting (>= 1). Used to pick interpolation strength.
    /// </summary>
    public static double MaxLinearDownscaleRatio(uint orientedWidth, uint orientedHeight, uint outputWidth, uint outputHeight)
    {
        var ow = Math.Max(1u, orientedWidth);
        var oh = Math.Max(1u, orientedHeight);
        var dw = Math.Max(1u, outputWidth);
        var dh = Math.Max(1u, outputHeight);
        return Math.Max(ow / (double)dw, oh / (double)dh);
    }

    /// <summary>
    /// Computes decode width/height in oriented pixel space for a single WIC <see cref="BitmapTransform"/> scale.
    /// </summary>
    /// <param name="orientedPixelWidth">Decoder <c>OrientedPixelWidth</c>.</param>
    /// <param name="orientedPixelHeight">Decoder <c>OrientedPixelHeight</c>.</param>
    /// <param name="targetBoxWidthPx">Physical pixel width of the view box.</param>
    /// <param name="targetBoxHeightPx">Physical pixel height of the view box.</param>
    /// <param name="maxDecodeEdgePx">Hard cap on the longer output edge (memory / worst-case alias guardrail).</param>
    /// <param name="kind">Fit, Fill, or full resolution (cap only).</param>
    public static (uint Width, uint Height) ComputeOutputDimensions(
        uint orientedPixelWidth,
        uint orientedPixelHeight,
        int targetBoxWidthPx,
        int targetBoxHeightPx,
        int maxDecodeEdgePx,
        BitmapUniformKind kind)
    {
        var ow = Math.Max(1u, orientedPixelWidth);
        var oh = Math.Max(1u, orientedPixelHeight);
        var tw = Math.Max(1, targetBoxWidthPx);
        var th = Math.Max(1, targetBoxHeightPx);

        double scale = kind switch
        {
            BitmapUniformKind.FullResolution => 1.0,
            BitmapUniformKind.Fit => Math.Min(tw / (double)ow, th / (double)oh),
            BitmapUniformKind.Fill => Math.Max(tw / (double)ow, th / (double)oh),
            _ => Math.Min(tw / (double)ow, th / (double)oh),
        };
        if (scale > 1.0)
            scale = 1.0;

        var outW = Math.Max(1u, (uint)Math.Round(ow * scale));
        var outH = Math.Max(1u, (uint)Math.Round(oh * scale));
        ApplyMaxEdgeCap(ref outW, ref outH, maxDecodeEdgePx);
        return (outW, outH);
    }

    private static void ApplyMaxEdgeCap(ref uint outW, ref uint outH, int maxDecodeEdgePx)
    {
        var cap = Math.Max(1, maxDecodeEdgePx);
        var m = Math.Max(outW, outH);
        if (m <= (uint)cap)
            return;

        var s = cap / (double)m;
        outW = Math.Max(1u, (uint)Math.Round(outW * s));
        outH = Math.Max(1u, (uint)Math.Round(outH * s));
    }
}
