using ImageHoard.Core.Services;
using ImageHoard.Core.Slideshow;

namespace ImageHoard.Tests;

public sealed class TreeSlideshowSessionTests
{
    [Fact]
    public async Task TreeSlideshow_yields_images_from_subtrees()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_ss_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "d"));
        await File.WriteAllTextAsync(Path.Combine(root, "1.jpg"), "a");
        await File.WriteAllTextAsync(Path.Combine(root, "d", "2.png"), "bb");

        try
        {
            var fs = new LocalFileSystem();
            var session = new TreeSlideshowSession(fs, new Random(42));
            session.Start(root);
            await session.WaitForInitialPoolAsync();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < 20; i++)
            {
                if (!session.TryMoveNext(out var p) || p == null)
                    break;
                seen.Add(Path.GetFileName(p));
            }

            Assert.Contains("1.jpg", seen);
            Assert.Contains("2.png", seen);
            session.StopEnumeration();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
