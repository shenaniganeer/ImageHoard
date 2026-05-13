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
}
