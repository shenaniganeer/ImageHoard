using ImageHoard.Core.Rename;

namespace ImageHoard.Tests;

public sealed class RenameTemplateTests
{
    [Fact]
    public void BuildPreview_auto_suffix_resolves_duplicate_names()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ih_rename_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var a = Path.Combine(tmp, "a.jpg");
            var b = Path.Combine(tmp, "b.jpg");
            File.WriteAllText(a, "x");
            File.WriteAllText(b, "y");

            var rows = RenameTemplate.BuildPreview(
                new[] { a, b },
                "{OriginalName}{Ext}",
                tmp,
                RenameCollisionPolicy.AutoSuffix);

            Assert.Equal(2, rows.Count);
            Assert.Equal(RenamePreviewStatus.Ok, rows[0].Status);
            Assert.Equal(RenamePreviewStatus.Ok, rows[1].Status);
            Assert.NotEqual(rows[0].DestinationPath, rows[1].DestinationPath);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void BuildPreview_abort_marks_collision()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ih_rename2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var a = Path.Combine(tmp, "a.jpg");
            var b = Path.Combine(tmp, "b.jpg");
            File.WriteAllText(a, "x");
            File.WriteAllText(b, "y");

            var rows = RenameTemplate.BuildPreview(
                new[] { a, b },
                "fixed{Ext}",
                tmp,
                RenameCollisionPolicy.Abort);

            Assert.Contains(rows, r => r.Status == RenamePreviewStatus.Collision);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }
}
