using System.Text.Json;

namespace ImageHoard.Tests;

public sealed class KeyboardProfileFindBindingTests
{
    [Fact]
    public void Keyboard_only_profile_includes_browse_findInTree_with_Control_F()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "defaults",
            "input-profiles",
            "keyboard-only.v1.json");
        Assert.True(File.Exists(path), $"Expected test output to include {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var bindings = doc.RootElement.GetProperty("bindings");
        Assert.True(bindings.TryGetProperty("browse.findInTree", out var find));
        Assert.Equal(JsonValueKind.Array, find.ValueKind);
        Assert.True(find.GetArrayLength() >= 1);
        var chord = find[0];
        Assert.Equal("keyboard", chord.GetProperty("kind").GetString());
        var keys = chord.GetProperty("keys");
        var keyList = keys.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Control", keyList);
        Assert.Contains("KeyF", keyList);
    }
}
