using System.IO;
using ImageHoard.Core.Browse;

namespace ImageHoard.Tests;

public sealed class BrowserTreeDeletePathDedupeTests
{
    [Fact]
    public void BuildDeletionPathLists_removes_file_under_selected_folder()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ImageHoardDedupe", Guid.NewGuid().ToString("N")));
        var sub = Path.Combine(root, "Sub");
        var file = Path.Combine(sub, "a.jpg");

        var (files, folders) = BrowserTreeDeletePathDedupe.BuildDeletionPathLists(
            new[] { file },
            new[] { sub });

        Assert.Empty(files);
        Assert.Single(folders);
        Assert.Equal(Path.GetFullPath(sub), Path.GetFullPath(folders[0]));
    }

    [Fact]
    public void BuildDeletionPathLists_removes_nested_selected_folder_when_parent_selected()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ImageHoardDedupe", Guid.NewGuid().ToString("N")));
        var parent = Path.Combine(root, "P");
        var child = Path.Combine(parent, "C");

        var (files, folders) = BrowserTreeDeletePathDedupe.BuildDeletionPathLists(
            Array.Empty<string>(),
            new[] { parent, child });

        Assert.Empty(files);
        Assert.Single(folders);
        Assert.Equal(Path.GetFullPath(parent), Path.GetFullPath(folders[0]));
    }

    [Fact]
    public void BuildDeletionPathLists_orders_folders_deepest_first()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ImageHoardDedupe", Guid.NewGuid().ToString("N")));
        var a = Path.Combine(root, "A");
        var b = Path.Combine(root, "B");

        var (files, folders) = BrowserTreeDeletePathDedupe.BuildDeletionPathLists(
            Array.Empty<string>(),
            new[] { a, b });

        Assert.Equal(2, folders.Count);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(folders[0]),
            Path.GetFullPath(folders[1]),
        };
        Assert.Contains(Path.GetFullPath(a), set);
        Assert.Contains(Path.GetFullPath(b), set);

        var deep = Path.Combine(a, "Inner");
        var (files2, folders2) = BrowserTreeDeletePathDedupe.BuildDeletionPathLists(
            Array.Empty<string>(),
            new[] { a, deep });
        Assert.Single(folders2);
        Assert.Equal(Path.GetFullPath(a), Path.GetFullPath(folders2[0]));
    }
}
