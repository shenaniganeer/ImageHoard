using ImageHoard.Core.Metrics;
using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class FolderMetricsScannerTests
{
    [Fact]
    public async Task ScanSubtreeAsync_counts_files_and_images()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_metrics_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        await File.WriteAllTextAsync(Path.Combine(root, "a.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "sub", "b.png"), "yy");
        await File.WriteAllTextAsync(Path.Combine(root, "sub", "readme.txt"), "zzz");

        try
        {
            var fs = new LocalFileSystem();
            var snap = await FolderMetricsScanner.ScanSubtreeAsync(fs, root);
            Assert.Equal(3, snap.TotalFileCount);
            Assert.Equal(2, snap.ImageFileCount);
            Assert.True(snap.AggregateSizeBytes >= 5);
            Assert.Equal(FolderMetricsScanScope.FullSubtree, snap.ScanScope);
            Assert.Null(snap.HasExpandableChildren);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanImmediateFilesAsync_counts_only_direct_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_metrics_imm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        await File.WriteAllTextAsync(Path.Combine(root, "a.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "sub", "b.png"), "yy");

        try
        {
            var fs = new LocalFileSystem();
            var snap = await FolderMetricsScanner.ScanImmediateFilesAsync(fs, root);
            Assert.Equal(FolderMetricsScanScope.ImmediateChildren, snap.ScanScope);
            Assert.Equal(1, snap.TotalFileCount);
            Assert.Equal(1, snap.ImageFileCount);
            Assert.True(snap.AggregateSizeBytes >= 1);
            Assert.True(snap.HasExpandableChildren);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanImmediateFilesAsync_HasExpandableChildren_false_when_empty()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_metrics_empty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fs = new LocalFileSystem();
            var snap = await FolderMetricsScanner.ScanImmediateFilesAsync(fs, root);
            Assert.Equal(0, snap.TotalFileCount);
            Assert.Equal(false, snap.HasExpandableChildren);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanImmediateFilesAsync_HasExpandableChildren_true_when_only_subdirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_metrics_expand_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        try
        {
            var fs = new LocalFileSystem();
            var snap = await FolderMetricsScanner.ScanImmediateFilesAsync(fs, root);
            Assert.Equal(0, snap.TotalFileCount);
            Assert.True(snap.HasExpandableChildren);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
