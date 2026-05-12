using System.Text.Json;
using ImageHoard.Core.Input;

namespace ImageHoard.Tests;

public sealed class InputPointerChordMatchTests
{
    [Fact]
    public void IsMouseWheelMatch_accepts_schema_without_modifiers()
    {
        var json = """{"kind":"mouseWheel","wheel":"Down"}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, false));
        Assert.False(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, true));
    }

    [Fact]
    public void IsMouseWheelMatch_respects_modifiers_array()
    {
        var json = """{"kind":"mouseWheel","wheel":"Up","modifiers":["Control"]}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseWheelMatch(chord, false, true, false, false, true));
        Assert.False(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, true));
    }

    [Fact]
    public void IsMouseButtonMatch_accepts_left_single_click()
    {
        var json = """{"kind":"mouseButton","button":"Left","clickCount":1}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseButtonMatch(chord, "Left", 1, false, false, false, false));
        Assert.False(InputPointerChordMatch.IsMouseButtonMatch(chord, "Right", 1, false, false, false, false));
    }

    [Fact]
    public void IsMouseButtonMatch_shift_left_matches_view_pan_preview_shipped_chord()
    {
        var json = """{"kind":"mouseButton","button":"Left","clickCount":1,"modifiers":["Shift"]}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseButtonMatch(chord, "Left", 1, true, false, false, false));
        Assert.False(InputPointerChordMatch.IsMouseButtonMatch(chord, "Left", 1, false, false, false, false));
        Assert.False(InputPointerChordMatch.IsMouseButtonMatch(chord, "Left", 1, true, true, false, false));
    }

    [Fact]
    public void FindChordKeyConflicts_detects_duplicate_mouseWheel()
    {
        var doc = new InputProfileDocument
        {
            SchemaVersion = 1,
            ProfileId = "test",
            DisplayName = "t",
            Bindings = new Dictionary<string, List<JsonElement>>(),
        };
        var json = """{"kind":"mouseWheel","wheel":"Down"}""";
        var el = JsonSerializer.Deserialize<JsonElement>(json);
        doc.Bindings!["nav.nextImage"] = new List<JsonElement> { el };
        doc.Bindings["nav.prevImage"] = new List<JsonElement> { el.Clone() };

        var issues = InputBindingConflictChecker.FindChordKeyConflicts(doc);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void EnumerateMouseButtonBindings_returns_only_mouseButton_kinds()
    {
        var doc = new InputProfileDocument
        {
            SchemaVersion = 1,
            ProfileId = "t",
            DisplayName = "t",
            Bindings = new Dictionary<string, List<JsonElement>>
            {
                ["a"] =
                [
                    JsonSerializer.Deserialize<JsonElement>("""{"kind":"keyboard","keys":["KeyA"]}"""),
                    JsonSerializer.Deserialize<JsonElement>("""{"kind":"mouseButton","button":"Middle","clickCount":1}"""),
                ],
            },
        };

        var list = InputPointerChordMatch.EnumerateMouseButtonBindings(doc).ToList();
        Assert.Single(list);
        Assert.Equal("a", list[0].CommandId);
    }
}
