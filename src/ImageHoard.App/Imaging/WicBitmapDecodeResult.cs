using Windows.Graphics.Imaging;

namespace ImageHoard.App.Imaging;

/// <summary>Result of <see cref="WicBitmapLoader.DecodeWithOrientationAsync"/> including oriented container dimensions.</summary>
public readonly record struct WicBitmapDecodeResult(SoftwareBitmap? Bitmap, uint OrientedPixelWidth, uint OrientedPixelHeight);
