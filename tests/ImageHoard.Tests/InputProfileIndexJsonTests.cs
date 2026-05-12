using System.Text.Json;

namespace ImageHoard.Tests;

public sealed class InputProfileIndexJsonTests
{
    [Fact]
    public void Default_input_profiles_index_has_builtin_profiles()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "defaults",
            "input-profiles",
            "index.json");
        Assert.True(File.Exists(path), $"Expected test output to include {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Number, root.GetProperty("schemaVersion").ValueKind);
        var profiles = root.GetProperty("builtinProfiles");
        Assert.Equal(JsonValueKind.Array, profiles.ValueKind);
        Assert.True(profiles.GetArrayLength() >= 1);
        var first = profiles[0];
        Assert.True(first.TryGetProperty("profileId", out var id) && id.GetString()?.Length > 0);
        Assert.True(first.TryGetProperty("path", out var rel) && rel.GetString()?.Length > 0);
    }
}
