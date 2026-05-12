namespace ImageHoard.Core.Imaging;

/// <summary>
/// How a bitmap should be dimensioned to match a rectangular target in physical pixels (WinUI stretch modes).
/// </summary>
public enum BitmapUniformKind
{
    /// <summary>Uniform: entire image visible (scale = min of axis ratios).</summary>
    Fit,

    /// <summary>UniformToFill: image covers the target (scale = max of axis ratios).</summary>
    Fill,

    /// <summary>No target-based downscale; output is oriented size capped by max edge only.</summary>
    FullResolution,
}
