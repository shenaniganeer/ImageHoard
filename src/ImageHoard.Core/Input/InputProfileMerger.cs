using System.Text.Json;

namespace ImageHoard.Core.Input;

/// <summary>Merges built-in profile JSON with user override bindings (partial document).</summary>
public static class InputProfileMerger
{
    /// <summary>
    /// <paramref name="userOverridesJson"/> may be null/empty, or a partial JSON object with only <c>bindings</c>.
    /// User bindings replace the same <c>commandId</c> lists entirely when present.
    /// </summary>
    public static InputProfileDocument MergeWithUserOverrides(InputProfileDocument @base, string? userOverridesJson)
    {
        var merged = CloneShallow(@base);
        if (string.IsNullOrWhiteSpace(userOverridesJson))
            return merged;

        try
        {
            using var doc = JsonDocument.Parse(userOverridesJson);
            if (!doc.RootElement.TryGetProperty("bindings", out var b) || b.ValueKind != JsonValueKind.Object)
                return merged;

            merged.Bindings ??= new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
            foreach (var prop in b.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    continue;
                var list = new List<JsonElement>();
                foreach (var item in prop.Value.EnumerateArray())
                    list.Add(item.Clone());
                merged.Bindings[prop.Name] = list;
            }
        }
        catch
        {
            // ignore malformed user file
        }

        return merged;
    }

    /// <summary>
    /// Union of <paramref name="primary"/> and <paramref name="secondary"/> bindings per command;
    /// chords already present (same raw JSON) are not duplicated.
    /// </summary>
    public static InputProfileDocument MergeBindingLists(InputProfileDocument primary, InputProfileDocument secondary)
    {
        var merged = CloneShallow(primary);
        merged.ProfileId = "Combined";
        merged.DisplayName = "Keyboard and mouse defaults";
        merged.SchemaVersion = Math.Max(primary.SchemaVersion, secondary.SchemaVersion);
        merged.Bindings ??= new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);

        if (secondary.Bindings == null)
            return merged;

        foreach (var (commandId, secondaryChords) in secondary.Bindings)
        {
            if (!merged.Bindings!.TryGetValue(commandId, out var existing))
            {
                merged.Bindings[commandId] = secondaryChords.Select(c => c.Clone()).ToList();
                continue;
            }

            foreach (var el in secondaryChords)
            {
                var raw = el.GetRawText();
                if (existing.Any(c => string.Equals(c.GetRawText(), raw, StringComparison.Ordinal)))
                    continue;
                existing.Add(el.Clone());
            }
        }

        return merged;
    }

    public static InputProfileDocument CloneShallow(InputProfileDocument source) =>
        new()
        {
            SchemaVersion = source.SchemaVersion,
            ProfileId = source.ProfileId,
            DisplayName = source.DisplayName,
            Bindings = CloneBindings(source.Bindings)
                       ?? new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal),
        };

    public static Dictionary<string, List<JsonElement>>? CloneBindings(Dictionary<string, List<JsonElement>>? source)
    {
        if (source == null)
            return null;
        var copy = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var kv in source)
        {
            var list = new List<JsonElement>(kv.Value.Count);
            foreach (var el in kv.Value)
                list.Add(el.Clone());
            copy[kv.Key] = list;
        }

        return copy;
    }

    /// <summary>Serializes only the bindings object for user override file.</summary>
    public static string SerializeBindingsOnly(Dictionary<string, List<JsonElement>> bindings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WritePropertyName("bindings");
            writer.WriteStartObject();
            foreach (var kv in bindings.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(kv.Key);
                writer.WriteStartArray();
                foreach (var el in kv.Value)
                    el.WriteTo(writer);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Dictionary<string, List<JsonElement>>? DeserializeBindingsObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty("bindings", out var b) || b.ValueKind != JsonValueKind.Object)
                return null;
            var dict = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
            foreach (var prop in b.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    continue;
                var list = new List<JsonElement>();
                foreach (var item in prop.Value.EnumerateArray())
                    list.Add(item.Clone());
                dict[prop.Name] = list;
            }

            return dict;
        }
        catch
        {
            return null;
        }
    }
}
