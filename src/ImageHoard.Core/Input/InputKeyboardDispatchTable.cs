using System.Text.Json;

namespace ImageHoard.Core.Input;

/// <summary>Builds ordered keyboard dispatch rows from a merged profile.</summary>
public sealed class InputKeyboardDispatchTable
{
    private readonly List<(string CommandId, string[] Keys)> _rows;

    private InputKeyboardDispatchTable(List<(string CommandId, string[] Keys)> rows) =>
        _rows = rows;

    public static InputKeyboardDispatchTable FromProfile(InputProfileDocument doc)
    {
        var rows = new List<(string, string[])>();
        if (doc.Bindings != null)
        {
            foreach (var kv in doc.Bindings)
            {
                foreach (var chord in kv.Value)
                {
                    if (!TryGetKeyboardChordKeys(chord, out var keys))
                        continue;
                    rows.Add((kv.Key, keys));
                }
            }
        }

        return new InputKeyboardDispatchTable(rows);
    }

    public static bool TryGetKeyboardChordKeys(JsonElement chord, out string[] keys)
    {
        keys = Array.Empty<string>();
        if (!chord.TryGetProperty("kind", out var k) || !string.Equals(k.GetString(), "keyboard", StringComparison.Ordinal))
            return false;
        if (!chord.TryGetProperty("keys", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrEmpty(s))
                list.Add(s);
        }

        if (list.Count == 0)
            return false;
        keys = list.ToArray();
        return true;
    }

    /// <summary>First matching command wins (JSON declaration order).</summary>
    public string? TryMatchFirst(in KeyboardChordState state)
    {
        foreach (var (commandId, keys) in _rows)
        {
            if (KeyboardChordMatcher.Matches(keys, state))
                return commandId;
        }

        return null;
    }
}
