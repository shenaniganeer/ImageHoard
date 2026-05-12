using System.Text.Json;
using ImageHoard.Core.Input;

namespace ImageHoard.Tests;

public sealed class InputProfileMergerTests
{
    [Fact]
    public void MergeWithUserOverrides_replaces_command_bindings()
    {
        var elA = JsonSerializer.Deserialize<JsonElement>("""
{"kind":"keyboard","keys":["KeyA"]}
""");
        var doc = new InputProfileDocument
        {
            SchemaVersion = 1,
            ProfileId = "KeyboardOnly",
            DisplayName = "x",
            Bindings = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal)
            {
                ["nav.nextImage"] = new List<JsonElement> { elA },
            },
        };
        var user = """{"schemaVersion":1,"bindings":{"nav.nextImage":[{"kind":"keyboard","keys":["KeyB"]}]}}""";
        var merged = InputProfileMerger.MergeWithUserOverrides(doc, user);
        Assert.NotNull(merged.Bindings);
        Assert.True(merged.Bindings.TryGetValue("nav.nextImage", out var chords));
        Assert.Single(chords);
        Assert.Contains("KeyB", chords[0].GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeBindingsOnly_round_trips()
    {
        var dict = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal)
        {
            ["nav.prevImage"] =
            [
                JsonSerializer.Deserialize<JsonElement>("""
{"kind":"keyboard","keys":["ArrowLeft"]}
""")!,
            ],
        };
        var s = InputProfileMerger.SerializeBindingsOnly(dict);
        var parsed = InputProfileMerger.DeserializeBindingsObject(s);
        Assert.NotNull(parsed);
        Assert.True(parsed.TryGetValue("nav.prevImage", out var list));
        Assert.Single(list);
    }

    [Fact]
    public void MergeBindingLists_unions_chords_and_dedupes_by_raw_json()
    {
        var kbA = JsonSerializer.Deserialize<JsonElement>("""{"kind":"keyboard","keys":["KeyA"]}""");
        var kbB = JsonSerializer.Deserialize<JsonElement>("""{"kind":"keyboard","keys":["KeyB"]}""");
        var kbADup = JsonSerializer.Deserialize<JsonElement>("""{"kind":"keyboard","keys":["KeyA"]}""");
        var primary = new InputProfileDocument
        {
            SchemaVersion = 1,
            ProfileId = "KeyboardOnly",
            DisplayName = "K",
            Bindings = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal)
            {
                ["nav.nextImage"] = new List<JsonElement> { kbA },
                ["sort.flagKeep"] = new List<JsonElement> { kbB },
            },
        };
        var secondary = new InputProfileDocument
        {
            SchemaVersion = 1,
            ProfileId = "MouseOnly",
            DisplayName = "M",
            Bindings = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal)
            {
                ["nav.nextImage"] = new List<JsonElement> { kbADup, kbB },
                ["nav.prevImage"] = new List<JsonElement> { kbB },
            },
        };

        var merged = InputProfileMerger.MergeBindingLists(primary, secondary);
        Assert.Equal("Combined", merged.ProfileId);
        Assert.Equal("Keyboard and mouse defaults", merged.DisplayName);
        Assert.NotNull(merged.Bindings);
        Assert.True(merged.Bindings.TryGetValue("nav.nextImage", out var next));
        Assert.Equal(2, next.Count);
        Assert.True(merged.Bindings.TryGetValue("sort.flagKeep", out var keep));
        Assert.Single(keep);
        Assert.True(merged.Bindings.TryGetValue("nav.prevImage", out var prev));
        Assert.Single(prev);
    }
}
