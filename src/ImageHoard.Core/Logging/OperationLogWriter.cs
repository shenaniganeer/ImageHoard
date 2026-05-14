using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageHoard.Core.Logging;

public sealed class OperationLogBatchRecord
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("utc")]
    public DateTime Utc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("summary")]
    public OperationLogSummary Summary { get; set; } = new();

    [JsonPropertyName("entries")]
    public List<OperationLogEntry> Entries { get; set; } = new();
}

public sealed class OperationLogSummary
{
    [JsonPropertyName("ok")]
    public int Ok { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }
}

public sealed class OperationLogEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("result")]
    public string Result { get; set; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

/// <summary>FR-SR-09 append-only JSONL.</summary>
public static class OperationLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task AppendAsync(string jsonlPath, OperationLogBatchRecord record, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(jsonlPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        await File.AppendAllTextAsync(jsonlPath, line, cancellationToken).ConfigureAwait(true);
    }
}
