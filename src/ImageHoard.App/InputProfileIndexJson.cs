using System.Text.Json.Serialization;

namespace ImageHoard.App;

internal sealed class InputProfileIndexDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("builtinProfiles")]
    public List<InputProfileBuiltinEntry>? BuiltinProfiles { get; set; }
}

internal sealed class InputProfileBuiltinEntry
{
    [JsonPropertyName("profileId")]
    public string? ProfileId { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
