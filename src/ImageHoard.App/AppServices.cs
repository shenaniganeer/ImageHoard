using ImageHoard.Core.Services;

namespace ImageHoard.App;

public static class AppServices
{
    public static IFileSystem FileSystem { get; } = new LocalFileSystem();
}
