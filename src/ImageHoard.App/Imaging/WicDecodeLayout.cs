using ImageHoard.Core.Imaging;

namespace ImageHoard.App.Imaging;

/// <summary>
/// Physical-pixel decode target for WIC downscale (FR-VW-02, anti-alias / moiré mitigation).
/// List thumbnails (32–48 px): use a small <see cref="TargetBoxWidthPx"/> / <see cref="TargetBoxHeightPx"/>
/// with <see cref="BitmapUniformKind.Fit"/>—never full-decode then GPU-shrink to a tiny tile.
/// </summary>
public readonly record struct WicDecodeLayout(
    int TargetBoxWidthPx,
    int TargetBoxHeightPx,
    BitmapUniformKind UniformKind,
    int MaxDecodeEdgePx = 8192);
