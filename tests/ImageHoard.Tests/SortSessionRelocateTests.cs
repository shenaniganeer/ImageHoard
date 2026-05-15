using ImageHoard.Core.Sort;

namespace ImageHoard.Tests;

public sealed class SortSessionRelocateTests
{
    [Fact]
    public void RelocatePathsForDirectoryRename_remaps_files_in_folder()
    {
        var oldRoot = Path.Combine(Path.GetTempPath(), "ih_reloc_" + Guid.NewGuid().ToString("N"));
        var newRoot = oldRoot + "_renamed";
        var oldFile = Path.Combine(oldRoot, "pic.jpg");
        var newFile = Path.Combine(newRoot, "pic.jpg");

        var session = new SortSession();
        session.SetState(oldFile, SortFlagState.Keep);

        session.RelocatePathsForDirectoryRename(oldRoot, newRoot);

        Assert.Equal(SortFlagState.Unset, session.GetState(oldFile));
        Assert.Equal(SortFlagState.Keep, session.GetState(newFile));
    }

    [Fact]
    public void RelocatePathsForDirectoryRename_remaps_nested_files()
    {
        var oldRoot = Path.Combine(Path.GetTempPath(), "ih_reloc2_" + Guid.NewGuid().ToString("N"));
        var newRoot = oldRoot + "_x";
        var oldNested = Path.Combine(oldRoot, "sub", "a.jpg");
        var newNested = Path.Combine(newRoot, "sub", "a.jpg");

        var session = new SortSession();
        session.SetState(oldNested, SortFlagState.Delete);

        session.RelocatePathsForDirectoryRename(oldRoot, newRoot);

        Assert.Equal(SortFlagState.Delete, session.GetState(newNested));
        Assert.Equal(SortFlagState.Unset, session.GetState(oldNested));
    }

    [Fact]
    public void RelocateImagePath_remaps_single_file_key()
    {
        var session = new SortSession();
        var oldPath = @"C:\share\album\old\pic.jpg";
        var newPath = @"C:\share\album\new\pic.jpg";
        session.SetState(oldPath, SortFlagState.Keep);

        session.RelocateImagePath(oldPath, newPath);

        Assert.Equal(SortFlagState.Unset, session.GetState(oldPath));
        Assert.Equal(SortFlagState.Keep, session.GetState(newPath));
    }

    [Fact]
    public void RelocateImagePath_noop_when_old_key_missing()
    {
        var session = new SortSession();
        session.RelocateImagePath(@"C:\a.jpg", @"C:\b.jpg");
        Assert.Equal(SortFlagState.Unset, session.GetState(@"C:\b.jpg"));
    }

    [Fact]
    public void RelocateImagePath_noop_when_paths_equal_ignore_case()
    {
        var session = new SortSession();
        var p = @"C:\folder\Pic.JPG";
        session.SetState(p, SortFlagState.Delete);
        session.RelocateImagePath(p, @"c:\folder\pic.jpg");
        Assert.Equal(SortFlagState.Delete, session.GetState(p));
    }
}
