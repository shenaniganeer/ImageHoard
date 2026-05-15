using ImageHoard.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ImageHoard.App.Imaging;

/// <summary>
/// WIC-backed decode with EXIF orientation (FR-VW-02). Downscale uses explicit interpolation modes
/// (never <see cref="BitmapInterpolationMode.NearestNeighbor"/>). Residual GPU scaling in WinUI
/// <see cref="Microsoft.UI.Xaml.Controls.Image"/> with <c>Stretch.Uniform</c> is typically bilinear on the compositor;
/// decode-to-fit minimizes heavy minification on that path.
/// </summary>
public static class WicBitmapLoader
{
    /// <summary>Full-resolution decode (no target box); heaviest path.</summary>
    public static Task<SoftwareBitmap?> DecodeWithOrientationAsync(string absolutePath) =>
        DecodeWithOrientationAsync(absolutePath, layout: null);

    /// <summary>
    /// When <paramref name="layout"/> is null, decodes at full oriented pixel size (legacy behavior).
    /// Otherwise scales once in WIC to match the layout box and max-edge policy.
    /// </summary>
    public static async Task<SoftwareBitmap?> DecodeWithOrientationAsync(string absolutePath, WicDecodeLayout? layout)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(absolutePath);
            using IRandomAccessStream stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var orientedW = decoder.OrientedPixelWidth;
            var orientedH = decoder.OrientedPixelHeight;
            if (orientedW == 0 || orientedH == 0)
                return null;

            var transform = new BitmapTransform();
            if (layout != null)
            {
                var (outW, outH) = BitmapDecodeFit.ComputeOutputDimensions(
                    orientedW,
                    orientedH,
                    layout.Value.TargetBoxWidthPx,
                    layout.Value.TargetBoxHeightPx,
                    layout.Value.MaxDecodeEdgePx,
                    layout.Value.UniformKind);

                var needsScale = outW != orientedW || outH != orientedH;
                if (needsScale)
                {
                    transform.ScaledWidth = outW;
                    transform.ScaledHeight = outH;
                    var ratio = BitmapDecodeFit.MaxLinearDownscaleRatio(orientedW, orientedH, outW, outH);
                    transform.InterpolationMode = WicBitmapInterpolation.ForDownscale(ratio);
                }
            }

            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads oriented pixel dimensions from the container without decoding pixels (metadata only).
    /// Returns (0,0) on failure.
    /// </summary>
    public static async Task<(uint Width, uint Height)> GetOrientedPixelDimensionsAsync(string absolutePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(absolutePath);
            using IRandomAccessStream stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var w = decoder.OrientedPixelWidth;
            var h = decoder.OrientedPixelHeight;
            if (w == 0 || h == 0)
                return (0, 0);
            return (w, h);
        }
        catch
        {
            return (0, 0);
        }
    }
}
