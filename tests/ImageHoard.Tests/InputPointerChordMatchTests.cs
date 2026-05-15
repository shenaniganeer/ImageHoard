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
    public void IsMouseWheelMatch_without_heldButtons_ignores_pressed_mouse_buttons()
    {
        var json = """{"kind":"mouseWheel","wheel":"Down"}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        var pressed = new[] { "X1" };
        Assert.True(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, false, pressed));
    }

    [Fact]
    public void IsMouseWheelMatch_heldButtons_requires_exact_sorted_set()
    {
        var json = """{"kind":"mouseWheel","wheel":"Up","heldButtons":["X1"]}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.MouseWheelSpecifiesHeldButtons(chord));
        Assert.False(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, true, ReadOnlySpan<string>.Empty));
        Assert.False(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, true, new[] { "Left" }));
        Assert.True(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, true, new[] { "X1" }));
        Assert.False(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, true, new[] { "Left", "X1" }));
    }

    [Fact]
    public void IsMouseWheelMatch_heldButtons_duplicate_entries_normalize()
    {
        var json = """{"kind":"mouseWheel","wheel":"Up","heldButtons":["X1","X1"]}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, true, new[] { "X1" }));
    }

    [Fact]
    public void IsMouseWheelMatch_heldButtons_and_modifiers()
    {
        var json = """{"kind":"mouseWheel","wheel":"Down","heldButtons":["X1"],"modifiers":["Shift"]}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(InputPointerChordMatch.IsMouseWheelMatch(chord, false, false, false, false, false, new[] { "X1" }));
        Assert.True(InputPointerChordMatch.IsMouseWheelMatch(chord, true, false, false, false, false, new[] { "X1" }));
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
    public void IsMouseButtonMatch_respects_click_count_two_and_three()
    {
        var dbl = JsonSerializer.Deserialize<JsonElement>("""{"kind":"mouseButton","button":"Left","clickCount":2}""");
        var tpl = JsonSerializer.Deserialize<JsonElement>("""{"kind":"mouseButton","button":"Right","clickCount":3}""");
        Assert.True(InputPointerChordMatch.IsMouseButtonMatch(dbl, "Left", 2, false, false, false, false));
        Assert.False(InputPointerChordMatch.IsMouseButtonMatch(dbl, "Left", 1, false, false, false, false));
        Assert.False(InputPointerChordMatch.IsMouseButtonMatch(dbl, "Left", 3, false, false, false, false));
        Assert.True(InputPointerChordMatch.IsMouseButtonMatch(tpl, "Right", 3, false, false, false, false));
        Assert.False(InputPointerChordMatch.IsMouseButtonMatch(tpl, "Right", 2, false, false, false, false));
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
    public void IsMouseButtonMatch_default_click_count_is_one_when_omitted()
    {
        var json = """{"kind":"mouseButton","button":"Left"}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseButtonMatch(chord, "Left", 1, false, false, false, false));
        Assert.False(InputPointerChordMatch.IsMouseButtonMatch(chord, "Left", 2, false, false, false, false));
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
    public void IsMouseChordMatch_requires_exact_sorted_button_set()
    {
        var json = """{"kind":"mouseChord","buttons":["Left","Right"]}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseChordMatch(chord, false, false, false, false, new[] { "Left", "Right" }));
        Assert.False(InputPointerChordMatch.IsMouseChordMatch(chord, false, false, false, false, new[] { "Right", "Left" }));
        Assert.False(InputPointerChordMatch.IsMouseChordMatch(chord, false, false, false, false, new[] { "Left" }));
        Assert.False(InputPointerChordMatch.IsMouseChordMatch(chord, false, false, false, false, new[] { "Left", "Right", "X1" }));
    }

    [Fact]
    public void IsMouseChordMatch_respects_modifiers()
    {
        var json = """{"kind":"mouseChord","buttons":["Left","Right"],"modifiers":["Shift"]}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseChordMatch(chord, true, false, false, false, new[] { "Left", "Right" }));
        Assert.False(InputPointerChordMatch.IsMouseChordMatch(chord, false, false, false, false, new[] { "Left", "Right" }));
    }

    [Fact]
    public void IsMouseChordMatch_duplicate_buttons_in_json_normalize()
    {
        var json = """{"kind":"mouseChord","buttons":["Left","Left","Right"]}""";
        var chord = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(InputPointerChordMatch.IsMouseChordMatch(chord, false, false, false, false, new[] { "Left", "Right" }));
    }

    [Fact]
    public void EnumerateMouseChordBindings_returns_only_mouseChord_kinds()
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
                    JsonSerializer.Deserialize<JsonElement>("""{"kind":"mouseButton","button":"Left","clickCount":1}"""),
                    JsonSerializer.Deserialize<JsonElement>("""{"kind":"mouseChord","buttons":["Left","Right"]}"""),
                ],
            },
        };

        var list = InputPointerChordMatch.EnumerateMouseChordBindings(doc).ToList();
        Assert.Single(list);
        Assert.Equal("a", list[0].CommandId);
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
