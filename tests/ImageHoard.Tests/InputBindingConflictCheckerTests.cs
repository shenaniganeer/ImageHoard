using ImageHoard.Core.Input;

namespace ImageHoard.Tests;

public sealed class InputBindingConflictCheckerTests
{
    [Fact]
    public void FindChordKeyConflicts_detects_duplicate_keyboard_chords()
    {
        var doc = new InputProfileDocument
        {
            SchemaVersion = 1,
            ProfileId = "test",
            DisplayName = "t",
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };

        var json = """
{"kind":"keyboard","keys":["KeyA","Control"]}
""";
        var el = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        doc.Bindings!["nav.nextImage"] = new List<System.Text.Json.JsonElement> { el };
        doc.Bindings["nav.prevImage"] = new List<System.Text.Json.JsonElement> { el };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void FindChordKeyConflicts_mouseWheel_same_heldButtons_conflicts()
    {
        var doc = new InputProfileDocument
        {
            SchemaVersion = 1,
            ProfileId = "test",
            DisplayName = "t",
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };
        var json = """{"kind":"mouseWheel","wheel":"Up","heldButtons":["X1"]}""";
        var el = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        doc.Bindings!["nav.nextImage"] = new List<System.Text.Json.JsonElement> { el };
        doc.Bindings["nav.prevImage"] = new List<System.Text.Json.JsonElement> { el.Clone() };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void FindChordKeyConflicts_mouseWheel_different_heldButtons_no_conflict()
    {
        var doc = new InputProfileDocument
        {
            SchemaVersion = 1,
            ProfileId = "test",
            DisplayName = "t",
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };
        var a = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"kind":"mouseWheel","wheel":"Up","heldButtons":["X1"]}""");
        var b = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"kind":"mouseWheel","wheel":"Up","heldButtons":["X2"]}""");
        doc.Bindings!["nav.nextImage"] = new List<System.Text.Json.JsonElement> { a };
        doc.Bindings["nav.prevImage"] = new List<System.Text.Json.JsonElement> { b };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.Empty(issues);
    }

    [Fact]
    public void FindChordKeyConflicts_allows_exactly_two_distinct_nav_plus_browser_tree_keyboard_overlap()
    {
        var doc = new InputProfileDocument
        {
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };
        var arrowDown = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"kind":"keyboard","keys":["ArrowDown"]}""");
        doc.Bindings!["nav.nextImage"] = new List<System.Text.Json.JsonElement> { arrowDown };
        doc.Bindings["browse.treeNext"] =
            new List<System.Text.Json.JsonElement> { System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(arrowDown.GetRawText())! };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.Empty(issues);
    }

    [Fact]
    public void FindChordKeyConflicts_still_flags_nav_plus_two_tree_commands_on_same_chord()
    {
        var doc = new InputProfileDocument
        {
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };
        var arrowDown = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"kind":"keyboard","keys":["ArrowDown"]}""");
        var el = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(arrowDown.GetRawText())!;
        doc.Bindings!["nav.nextImage"] = new List<System.Text.Json.JsonElement> { arrowDown };
        doc.Bindings["browse.treeNext"] = new List<System.Text.Json.JsonElement> { el };
        doc.Bindings["browse.treeExpand"] = new List<System.Text.Json.JsonElement> { el };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void FindChordKeyConflicts_flags_two_tree_commands_sharing_keyboard_chord()
    {
        var doc = new InputProfileDocument
        {
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };
        var arrow = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"kind":"keyboard","keys":["ArrowDown"]}""")!;
        var el = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(arrow.GetRawText())!;
        doc.Bindings!["browse.treeNext"] = new List<System.Text.Json.JsonElement> { arrow };
        doc.Bindings["browse.treePrevious"] = new List<System.Text.Json.JsonElement> { el };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void FindChordKeyConflicts_still_flags_two_nav_commands_sharing_a_keyboard_chord()
    {
        var doc = new InputProfileDocument
        {
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };
        var arrowDown = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"kind":"keyboard","keys":["ArrowDown"]}""");
        var el = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(arrowDown.GetRawText())!;
        doc.Bindings!["nav.nextImage"] = new List<System.Text.Json.JsonElement> { arrowDown };
        doc.Bindings["nav.prevImage"] = new List<System.Text.Json.JsonElement> { el };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void FindChordKeyConflicts_same_mouse_button_browse_vs_slideshow_disjoint_masks_no_conflict()
    {
        var doc = new InputProfileDocument
        {
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };
        var x2 = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"kind":"mouseButton","button":"X2","clickCount":1}""");
        doc.Bindings!["sort.deleteArchiveWizard"] = new List<System.Text.Json.JsonElement> { x2 };
        doc.Bindings["slideshow.switchToBrowseAtCurrentLocation"] =
            new List<System.Text.Json.JsonElement> { System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(x2.GetRawText())! };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.Empty(issues);
    }

    [Fact]
    public void FindChordKeyConflicts_same_mouse_button_both_browse_scoped_still_conflicts()
    {
        var doc = new InputProfileDocument
        {
            Bindings = new Dictionary<string, List<System.Text.Json.JsonElement>>(),
        };
        var x2 = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"kind":"mouseButton","button":"X2","clickCount":1}""");
        doc.Bindings!["sort.deleteArchiveWizard"] = new List<System.Text.Json.JsonElement> { x2 };
        doc.Bindings["sort.clearAllFlags"] =
            new List<System.Text.Json.JsonElement> { System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(x2.GetRawText())! };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.NotEmpty(issues);
    }
}
