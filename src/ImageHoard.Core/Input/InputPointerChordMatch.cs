using System.Text.Json;

namespace ImageHoard.Core.Input;

/// <summary>Non-keyboard chord matching for pointer / wheel bindings (schema v1).</summary>
public static class InputPointerChordMatch
{
    public static bool IsMouseWheelMatch(JsonElement chord, bool shift, bool control, bool alt, bool win, bool wheelUp)
    {
        if (!chord.TryGetProperty("kind", out var k) || !string.Equals(k.GetString(), "mouseWheel", StringComparison.Ordinal))
            return false;
        if (!chord.TryGetProperty("wheel", out var w))
            return false;
        var wantUp = string.Equals(w.GetString(), "Up", StringComparison.Ordinal);
        if (wantUp != wheelUp)
            return false;
        return ModifiersMatch(chord, shift, control, alt, win);
    }

    public static bool IsMouseButtonMatch(JsonElement chord, string buttonName, int clickCount, bool shift, bool control, bool alt, bool win)
    {
        if (!chord.TryGetProperty("kind", out var k) || !string.Equals(k.GetString(), "mouseButton", StringComparison.Ordinal))
            return false;
        if (!chord.TryGetProperty("button", out var b))
            return false;
        if (!string.Equals(b.GetString(), buttonName, StringComparison.Ordinal))
            return false;
        var wantClicks = chord.TryGetProperty("clickCount", out var cc) ? cc.GetInt32() : 1;
        if (wantClicks != clickCount)
            return false;
        return ModifiersMatch(chord, shift, control, alt, win);
    }

    private static bool ModifiersMatch(JsonElement chord, bool shift, bool control, bool alt, bool win)
    {
        if (!chord.TryGetProperty("modifiers", out var mods) || mods.ValueKind != JsonValueKind.Array)
            return !shift && !control && !alt && !win;

        var wantShift = false;
        var wantCtrl = false;
        var wantAlt = false;
        var wantWin = false;
        foreach (var m in mods.EnumerateArray())
        {
            switch (m.GetString())
            {
                case "Shift": wantShift = true; break;
                case "Control": wantCtrl = true; break;
                case "Alt": wantAlt = true; break;
                case "Win": wantWin = true; break;
            }
        }

        return wantShift == shift && wantCtrl == control && wantAlt == alt && wantWin == win;
    }

    /// <summary>Enumerates (commandId, chord) for mouseWheel chords in profile order.</summary>
    public static IEnumerable<(string CommandId, JsonElement Chord)> EnumerateMouseWheelBindings(InputProfileDocument doc)
    {
        if (doc.Bindings == null)
            yield break;
        foreach (var kv in doc.Bindings)
        {
            foreach (var chord in kv.Value)
            {
                if (chord.TryGetProperty("kind", out var k)
                    && string.Equals(k.GetString(), "mouseWheel", StringComparison.Ordinal))
                    yield return (kv.Key, chord);
            }
        }
    }

    /// <summary>Enumerates (commandId, chord) for mouseButton chords in profile order.</summary>
    public static IEnumerable<(string CommandId, JsonElement Chord)> EnumerateMouseButtonBindings(InputProfileDocument doc)
    {
        if (doc.Bindings == null)
            yield break;
        foreach (var kv in doc.Bindings)
        {
            foreach (var chord in kv.Value)
            {
                if (chord.TryGetProperty("kind", out var k)
                    && string.Equals(k.GetString(), "mouseButton", StringComparison.Ordinal))
                    yield return (kv.Key, chord);
            }
        }
    }
}
