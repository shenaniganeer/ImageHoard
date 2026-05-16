using ImageHoard.Core.Browse;
using Xunit;

namespace ImageHoard.Tests;

public sealed class BrowserTreeViewportIntentResolverTests
{
    private static string CreateTempBrowseRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardTests", "ViewportIntent_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void ResolveBrowsedFolderPathForViewport_matches_BrowseContextDirectory_and_clamps_outside_root()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            var img = Path.Combine(sub, "a.jpg");
            File.WriteAllText(img, "x");

            var s = new BrowserPaneState(
                CurrentFolderPath: root,
                BrowseNavAnchorPath: null,
                LastSelectedImage: null,
                CurrentImageFullPath: img,
                TreeSelectedFolderPath: null,
                TreeSelectedImagePath: null);

            var browsed = BrowserTreeViewportIntentResolver.ResolveBrowsedFolderPathForViewport(s);
            Assert.Equal(Path.GetFullPath(sub), Path.GetFullPath(browsed!));

            var outside = Path.Combine(Path.GetTempPath(), "ImageHoardTests", "Other_" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(outside);
            try
            {
                var s2 = s with { BrowseNavAnchorPath = Path.Combine(outside, "z.jpg") };
                File.WriteAllText(s2.BrowseNavAnchorPath!, "z");
                var b2 = BrowserTreeViewportIntentResolver.ResolveBrowsedFolderPathForViewport(s2);
                Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(b2!));
            }
            finally
            {
                try
                {
                    Directory.Delete(outside, true);
                }
                catch
                {
                    // ignored
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Theory]
    [InlineData(BrowserTreeViewportReason.AfterWizardImageDeletes)]
    [InlineData(BrowserTreeViewportReason.AfterWizardNavigateToParent)]
    [InlineData(BrowserTreeViewportReason.AfterWizardUndo)]
    public void ForWizardCommit_pins_browsed_folder(BrowserTreeViewportReason reason)
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var sub = Path.Combine(root, "w");
            Directory.CreateDirectory(sub);
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var ctx = new BrowserTreeRefocusAfterWizardContext(sub, ImageDeletionWorkingFolder: null);
            var intent = BrowserTreeViewportIntentResolver.ForWizardCommit(state, reason, ctx);
            Assert.Equal(reason, intent.Reason);
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(intent.PrimaryPath!));
            Assert.Null(intent.SecondaryPath);
            Assert.Equal(0.0, intent.VerticalAlignmentRatio);
            Assert.False(intent.PreferSelectionFirst);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForWizardCommit_two_arg_overload_defaults_to_after_image_deletes()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForWizardCommit(state, refocusContext: null);
            Assert.Equal(BrowserTreeViewportReason.AfterWizardImageDeletes, intent.Reason);
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(intent.PrimaryPath!));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForWizardCommit_rejects_non_wizard_reason()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrowserTreeViewportIntentResolver.ForWizardCommit(default, BrowserTreeViewportReason.RootPopulate));
        Assert.Equal("reason", ex.ParamName);
    }

    [Fact]
    public void ForSiblingFolderNav_pins_target()
    {
        var target = Path.Combine(Path.GetTempPath(), "tgt_" + Guid.NewGuid().ToString("n"));
        var intent = BrowserTreeViewportIntentResolver.ForSiblingFolderNav(default, target);
        Assert.Equal(BrowserTreeViewportReason.SiblingFolderNavigation, intent.Reason);
        Assert.Equal(Path.GetFullPath(target), intent.PrimaryPath);
        Assert.False(intent.PreferSelectionFirst);
    }

    [Fact]
    public void ForFindHit_folder_under_root_sets_primary_and_prefers_selection()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var folder = Path.Combine(root, "f1");
            Directory.CreateDirectory(folder);
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var m = new BrowserFindMatch(folder, "f1", BrowserFindMatchKind.Folder);
            var intent = BrowserTreeViewportIntentResolver.ForFindHit(state, m);
            Assert.Equal(BrowserTreeViewportReason.FindHitFolder, intent.Reason);
            Assert.Equal(Path.GetFullPath(folder), Path.GetFullPath(intent.PrimaryPath!));
            Assert.True(intent.PreferSelectionFirst);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForFindHit_file_sets_primary_secondary_and_prefers_selection()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var file = Path.Combine(root, "x.png");
            File.WriteAllText(file, "");
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var m = new BrowserFindMatch(file, "x", BrowserFindMatchKind.File);
            var intent = BrowserTreeViewportIntentResolver.ForFindHit(state, m);
            Assert.Equal(BrowserTreeViewportReason.FindHitFile, intent.Reason);
            Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(intent.PrimaryPath!));
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(intent.SecondaryPath!));
            Assert.True(intent.PreferSelectionFirst);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForFindHit_outside_browse_root_returns_empty_primary()
    {
        var root = CreateTempBrowseRoot();
        var other = CreateTempBrowseRoot();
        try
        {
            var folder = Path.Combine(other, "outside");
            Directory.CreateDirectory(folder);
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var m = new BrowserFindMatch(folder, "o", BrowserFindMatchKind.Folder);
            var intent = BrowserTreeViewportIntentResolver.ForFindHit(state, m);
            Assert.Null(intent.PrimaryPath);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
                Directory.Delete(other, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForImageStep_sets_file_and_parent_secondary()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var sub = Path.Combine(root, "d");
            Directory.CreateDirectory(sub);
            var img = Path.Combine(sub, "p.jpg");
            File.WriteAllText(img, "");
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForImageStep(state, img);
            Assert.Equal(BrowserTreeViewportReason.ImageStep, intent.Reason);
            Assert.Equal(Path.GetFullPath(img), Path.GetFullPath(intent.PrimaryPath!));
            Assert.Equal(Path.GetFullPath(sub), Path.GetFullPath(intent.SecondaryPath!));
            Assert.True(intent.PreferSelectionFirst);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForRootPopulate_has_no_paths()
    {
        var intent = BrowserTreeViewportIntentResolver.ForRootPopulate(default);
        Assert.Equal(BrowserTreeViewportReason.RootPopulate, intent.Reason);
        Assert.Null(intent.PrimaryPath);
        Assert.Null(intent.SecondaryPath);
        Assert.Equal(0.0, intent.VerticalAlignmentRatio);
        Assert.False(intent.PreferSelectionFirst);
    }

    [Fact]
    public void ForColdBootAnchor_null_anchor_scrolls_top()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForColdBootAnchor(state, null);
            Assert.Equal(BrowserTreeViewportReason.ColdBootRestore, intent.Reason);
            Assert.Null(intent.PrimaryPath);
            Assert.Null(intent.SecondaryPath);
            Assert.Equal(0.0, intent.VerticalAlignmentRatio);
            Assert.False(intent.PreferSelectionFirst);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForColdBootAnchor_folder_pins_top()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var sub = Path.Combine(root, "d");
            Directory.CreateDirectory(sub);
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForColdBootAnchor(state, sub);
            Assert.Equal(BrowserTreeViewportReason.ColdBootRestore, intent.Reason);
            Assert.Equal(Path.GetFullPath(sub), Path.GetFullPath(intent.PrimaryPath!));
            Assert.Null(intent.SecondaryPath);
            Assert.Equal(0.0, intent.VerticalAlignmentRatio);
            Assert.False(intent.PreferSelectionFirst);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForColdBootAnchor_file_centers_with_parent_secondary()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var sub = Path.Combine(root, "d");
            Directory.CreateDirectory(sub);
            var img = Path.Combine(sub, "p.jpg");
            File.WriteAllText(img, "");
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForColdBootAnchor(state, img);
            Assert.Equal(BrowserTreeViewportReason.ColdBootRestore, intent.Reason);
            Assert.Equal(Path.GetFullPath(img), Path.GetFullPath(intent.PrimaryPath!));
            Assert.Equal(Path.GetFullPath(sub), Path.GetFullPath(intent.SecondaryPath!));
            Assert.Equal(0.5, intent.VerticalAlignmentRatio);
            Assert.True(intent.PreferSelectionFirst);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForColdBootAnchor_outside_browse_root_is_top()
    {
        var root = CreateTempBrowseRoot();
        var other = CreateTempBrowseRoot();
        try
        {
            var outsideFile = Path.Combine(other, "x.jpg");
            File.WriteAllText(outsideFile, "");
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForColdBootAnchor(state, outsideFile);
            Assert.Null(intent.PrimaryPath);
            Assert.Null(intent.SecondaryPath);
            Assert.Equal(0.0, intent.VerticalAlignmentRatio);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
                Directory.Delete(other, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForKeyboardMove_file_row_sets_secondary_parent()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var img = Path.Combine(root, "z.jpg");
            File.WriteAllText(img, "");
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForKeyboardMove(state, img);
            Assert.Equal(BrowserTreeViewportReason.KeyboardMove, intent.Reason);
            Assert.Equal(Path.GetFullPath(img), Path.GetFullPath(intent.PrimaryPath!));
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(intent.SecondaryPath!));
            Assert.True(intent.PreferSelectionFirst);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForKeyboardMove_folder_row_has_no_secondary()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var sub = Path.Combine(root, "k");
            Directory.CreateDirectory(sub);
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForKeyboardMove(state, sub);
            Assert.Equal(BrowserTreeViewportReason.KeyboardMove, intent.Reason);
            Assert.Equal(Path.GetFullPath(sub), Path.GetFullPath(intent.PrimaryPath!));
            Assert.Null(intent.SecondaryPath);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void ForFolderNavigation_matches_pin_path()
    {
        var root = CreateTempBrowseRoot();
        try
        {
            var state = new BrowserPaneState(root, null, null, null, null, null);
            var intent = BrowserTreeViewportIntentResolver.ForFolderNavigation(state);
            Assert.Equal(BrowserTreeViewportReason.FolderNavigation, intent.Reason);
            Assert.Equal(
                BrowserTreeViewportIntentResolver.GetPinPathAfterBrowseCommit(state),
                intent.PrimaryPath);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }
        }
    }
}
