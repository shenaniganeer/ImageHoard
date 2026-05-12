using ImageHoard.Core.Browse;

namespace ImageHoard.Tests;

public sealed class ImageExtensionsTests
{
    [Theory]
    [InlineData(@"C:\a\b\photo.JPEG", true)]
    [InlineData(@"\\server\share\img.PNG", true)]
    [InlineData(@"D:\x\readme.txt", false)]
    [InlineData(@"E:\y\noext", false)]
    public void IsImageFile_recognizes_extensions(string path, bool expected)
    {
        Assert.Equal(expected, ImageExtensions.IsImageFile(path));
    }

    [Fact]
    public void PickerFileTypeExtensions_lists_known_extensions()
    {
        Assert.Contains(".png", ImageExtensions.PickerFileTypeExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(ImageExtensions.PickerFileTypeExtensions);
    }
}
