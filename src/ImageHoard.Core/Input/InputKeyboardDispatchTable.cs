using System.Text.Json;

namespace ImageHoard.Core.Input;

/// <summary>Builds ordered keyboard dispatch rows from a merged profile.</summary>
public sealed class InputKeyboardDispatchTable
{
    private readonly List<(string CommandId, string[] Keys, InputBindingActivationMask Mask)> _rows;

    private InputKeyboardDispatchTable(List<(string CommandId, string[] Keys, InputBindingActivationMask Mask)> rows) =>
        _rows = rows;

    public static InputKeyboardDispatchTable FromProfile(InputProfileDocument doc) =>
        FromProfileExcludingCommandIds(doc, null);

    /// <summary>Like <see cref="FromProfile"/> but omits rows for any command id in <paramref name="excludedCommandIds"/> (when non-null).</summary>
    public static InputKeyboardDispatchTable FromProfileExcludingCommandIds(
        InputProfileDocument doc,
        IReadOnlySet<string>? excludedCommandIds)
    {
        var rows = new List<(string, string[], InputBindingActivationMask)>();
        if (doc.Bindings != null)
        {
            foreach (var kv in doc.Bindings)
            {
                if (excludedCommandIds != null && excludedCommandIds.Contains(kv.Key))
                    continue;
                foreach (var chord in kv.Value)
                {
                    if (!TryGetKeyboardChordKeys(chord, out var keys))
                        continue;
                    var mask = InputBindingActivation.ResolveMask(kv.Key, chord);
                    rows.Add((kv.Key, keys, mask));
                }
            }
        }

        return new InputKeyboardDispatchTable(rows);
    }

    /// <summary>Builds a table containing only the listed commands, in <paramref name="commandIdsInMatchOrder"/> order (chord order within each command preserved).</summary>
    public static InputKeyboardDispatchTable FromProfileIncludingCommandsInOrder(
        InputProfileDocument doc,
        ReadOnlySpan<string> commandIdsInMatchOrder)
    {
        var rows = new List<(string, string[], InputBindingActivationMask)>();
        if (doc.Bindings == null)
            return new InputKeyboardDispatchTable(rows);

        foreach (var commandId in commandIdsInMatchOrder)
        {
            if (!doc.Bindings.TryGetValue(commandId, out var chords))
                continue;
            foreach (var chord in chords)
            {
                if (!TryGetKeyboardChordKeys(chord, out var keys))
                    continue;
                var mask = InputBindingActivation.ResolveMask(commandId, chord);
                rows.Add((commandId, keys, mask));
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

    /// <summary>First matching command wins (JSON declaration order). Assumes browse UI (slideshow inactive).</summary>
    public string? TryMatchFirst(in KeyboardChordState state) =>
        TryMatchFirst(state, slideshowUiActive: false);

    /// <summary>First matching command wins (JSON declaration order).</summary>
    public string? TryMatchFirst(in KeyboardChordState state, bool slideshowUiActive)
    {
        foreach (var (commandId, keys, mask) in _rows)
        {
            if (!InputBindingActivation.MaskAppliesToUi(mask, slideshowUiActive))
                continue;
            if (KeyboardChordMatcher.Matches(keys, state))
                return commandId;
        }

        return null;
    }
}
