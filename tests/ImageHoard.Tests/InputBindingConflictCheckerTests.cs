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
}
