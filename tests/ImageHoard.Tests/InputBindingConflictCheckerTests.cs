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
}
