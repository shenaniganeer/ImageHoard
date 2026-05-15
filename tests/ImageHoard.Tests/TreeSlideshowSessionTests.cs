using ImageHoard.Core.Services;
using ImageHoard.Core.Slideshow;

namespace ImageHoard.Tests;

public sealed class TreeSlideshowSessionTests
{
    [Fact]
    public async Task TreeSlideshow_single_image_TryMoveNext_does_not_inflate_overlay_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_ss_one_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "only.jpg"), "x");

        try
        {
            var fs = new LocalFileSystem();
            var session = new TreeSlideshowSession(fs, new Random(0));
            session.Start(root);
            await session.WaitForInitialPoolAsync();

            Assert.True(session.TryMoveNext(out var first) && first != null);
            var path0 = session.CurrentPath;
            Assert.True(session.TryGetTreeOverlayPosition(out var i1, out var c1, out _));
            Assert.Equal(1, i1);
            Assert.Equal(1, c1);

            for (var step = 0; step < 10; step++)
            {
                Assert.True(session.TryMoveNext(out var p) && p != null);
                Assert.Equal(path0, session.CurrentPath, StringComparer.OrdinalIgnoreCase);
                Assert.True(session.TryGetTreeOverlayPosition(out var idx, out var hist, out _));
                Assert.Equal(1, idx);
                Assert.Equal(1, hist);
            }

            Assert.False(session.TryMovePrevious(out _));
            session.StopEnumeration();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    [Fact]
    public async Task TreeSlideshow_DiscoveredImageCount_and_overlay_metrics_after_first_slide()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_ss_disc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "d"));
        await File.WriteAllTextAsync(Path.Combine(root, "1.jpg"), "a");
        await File.WriteAllTextAsync(Path.Combine(root, "d", "2.png"), "bb");

        try
        {
            var fs = new LocalFileSystem();
            var session = new TreeSlideshowSession(fs, new Random(1));
            session.Start(root);
            await session.WaitForInitialPoolAsync();
            Assert.True(session.DiscoveredImageCount >= 2);
            Assert.False(session.TryGetTreeOverlayPosition(out _, out _, out var totalBefore));
            Assert.True(totalBefore >= 2);

            Assert.True(session.TryMoveNext(out var first));
            Assert.NotNull(first);
            Assert.True(session.TryGetTreeOverlayPosition(out var idx, out var histCount, out var discovered));
            Assert.Equal(1, idx);
            Assert.Equal(1, histCount);
            Assert.True(discovered >= 2);

            Assert.True(session.TryMoveNext(out _));
            Assert.True(session.TryMoveNext(out _));
            Assert.True(session.TryGetTreeOverlayPosition(out var idx3, out var hist3, out _));
            Assert.Equal(3, idx3);
            Assert.Equal(3, hist3);

            session.StopEnumeration();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TreeSlideshow_Previous_then_Next_redoes_forward_in_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_ss_hist_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "a.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "b.jpg"), "y");
        await File.WriteAllTextAsync(Path.Combine(root, "c.jpg"), "z");

        try
        {
            var fs = new LocalFileSystem();
            var session = new TreeSlideshowSession(fs, new Random(999));
            session.Start(root);
            await session.WaitForInitialPoolAsync();

            Assert.True(session.TryMoveNext(out var p1));
            Assert.True(session.TryMoveNext(out var p2));
            Assert.True(session.TryMoveNext(out var p3));
            Assert.NotNull(p1);
            Assert.NotNull(p2);
            Assert.NotNull(p3);

            Assert.True(session.TryMovePrevious(out var back1));
            Assert.Equal(p2, back1);
            Assert.True(session.TryMoveNext(out var redo));
            Assert.Equal(p3, redo);

            session.StopEnumeration();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TreeSlideshow_spill_store_reads_uniformly_across_many_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_ss_spill_" + Guid.NewGuid().ToString("N"));
        const int folderCount = 12;
        for (var i = 0; i < folderCount; i++)
        {
            var d = Path.Combine(root, "f" + i);
            Directory.CreateDirectory(d);
            await File.WriteAllTextAsync(Path.Combine(d, "x.jpg"), "p");
        }

        try
        {
            var fs = new LocalFileSystem();
            var session = new TreeSlideshowSession(fs, new Random(7), discoveredPathsInMemoryMaxOverride: 2);
            session.Start(root);
            await session.WaitForInitialPoolAsync();
            Assert.Equal(folderCount, session.DiscoveredImageCount);

            var foldersHit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var step = 0; step < 80; step++)
            {
                Assert.True(session.TryMoveNext(out var p) && p != null);
                var rel = Path.GetRelativePath(root, p!);
                foldersHit.Add(rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]);
            }

            Assert.True(foldersHit.Count >= 4, "Expected picks to span multiple spill-backed folders.");
            session.StopEnumeration();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
