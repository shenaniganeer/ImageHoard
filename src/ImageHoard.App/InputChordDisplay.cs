using System.Text.Json;

namespace ImageHoard.App;

internal static class InputChordDisplay
{
    internal const string ChordListSeparator = " · ";

    public static string FormatChord(JsonElement chord)
    {
        if (!chord.TryGetProperty("kind", out var kindProp))
            return chord.GetRawText();
        var kind = kindProp.GetString() ?? "";
        return kind switch
        {
            "keyboard" => FormatKeyboard(chord),
            "mouseWheel" => FormatMouseWheel(chord),
            "mouseButton" => FormatMouseButton(chord),
            "mouseWheelTilt" => FormatMouseWheelTilt(chord),
            "mouseChord" => FormatMouseChord(chord),
            _ => chord.GetRawText(),
        };
    }

    public static string FormatChordList(IReadOnlyList<JsonElement> list) =>
        list.Count == 0 ? "(none)" : string.Join(ChordListSeparator, list.Select(FormatChord));

    /// <summary>Half-open character spans for each chord in <see cref="FormatChordList"/> (non-empty lists only).</summary>
    public static List<(int Start, int EndExclusive)> GetChordListDisplayRanges(IReadOnlyList<JsonElement> list)
    {
        var result = new List<(int Start, int EndExclusive)>();
        if (list.Count == 0)
            return result;
        var pos = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var piece = FormatChord(list[i]);
            var start = pos;
            pos += piece.Length;
            result.Add((start, pos));
            if (i < list.Count - 1)
                pos += ChordListSeparator.Length;
        }

        return result;
    }

    private static string FormatKeyboard(JsonElement chord)
    {
        if (!chord.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            return chord.GetRawText();
        return string.Join("+", keys.EnumerateArray().Select(k => k.GetString() ?? ""));
    }

    private static string FormatMouseWheel(JsonElement chord)
    {
        var w = chord.TryGetProperty("wheel", out var wheel) ? wheel.GetString() ?? "?" : "?";
        var baseText = w is "Up" or "Down" ? $"Wheel {w}" : $"Wheel {w}";
        if (chord.TryGetProperty("heldButtons", out var hb) && hb.ValueKind == JsonValueKind.Array)
        {
            var held = hb.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            if (held.Length > 0)
                baseText = string.Join("+", held) + "+" + baseText;
        }

        return baseText + FormatModifiersSuffix(chord);
    }

    private static string FormatMouseButton(JsonElement chord)
    {
        var b = chord.TryGetProperty("button", out var btn) ? btn.GetString() ?? "?" : "?";
        var clicks = chord.TryGetProperty("clickCount", out var cc) ? cc.GetInt32() : 1;
        var clickLabel = clicks == 1 ? "click" : $"{clicks}x click";
        return $"{b} {clickLabel}{FormatModifiersSuffix(chord)}";
    }

    private static string FormatMouseWheelTilt(JsonElement chord)
    {
        var t = chord.TryGetProperty("tilt", out var tilt) ? tilt.GetString() ?? "?" : "?";
        return $"Tilt {t}{FormatModifiersSuffix(chord)}";
    }

    private static string FormatMouseChord(JsonElement chord)
    {
        if (!chord.TryGetProperty("buttons", out var buttons) || buttons.ValueKind != JsonValueKind.Array)
            return chord.GetRawText();
        var parts = buttons.EnumerateArray().Select(b => b.GetString() ?? "").Where(s => s.Length > 0).ToArray();
        return (parts.Length == 0 ? "Chord" : string.Join("+", parts)) + FormatModifiersSuffix(chord);
    }

    private static string FormatModifiersSuffix(JsonElement chord)
    {
        if (!chord.TryGetProperty("modifiers", out var mods) || mods.ValueKind != JsonValueKind.Array)
            return "";
        var parts = mods.EnumerateArray().Select(m => m.GetString() ?? "").Where(s => s.Length > 0).ToArray();
        return parts.Length == 0 ? "" : " (" + string.Join("+", parts) + ")";
    }
}
