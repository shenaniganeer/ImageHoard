using System.Text.Json;

namespace ImageHoard.Core.Input;

/// <summary>Non-keyboard chord matching for pointer / wheel bindings (schema v1).</summary>
public static class InputPointerChordMatch
{
    /// <summary>True when <paramref name="chord"/> has a non-empty <c>heldButtons</c> array (mouseWheel).</summary>
    public static bool MouseWheelSpecifiesHeldButtons(JsonElement chord)
    {
        if (!chord.TryGetProperty("kind", out var k) || !string.Equals(k.GetString(), "mouseWheel", StringComparison.Ordinal))
            return false;
        return TryGetChordHeldButtonsSorted(chord, out var sorted) && sorted.Length > 0;
    }

    public static bool IsMouseWheelMatch(JsonElement chord, bool shift, bool control, bool alt, bool win, bool wheelUp) =>
        IsMouseWheelMatch(chord, shift, control, alt, win, wheelUp, ReadOnlySpan<string>.Empty);

    /// <param name="pressedMouseButtonsSorted">Sorted unique schema button names currently down (e.g. from pointer properties); ignored when the chord has no <c>heldButtons</c> requirement.</param>
    public static bool IsMouseWheelMatch(
        JsonElement chord,
        bool shift,
        bool control,
        bool alt,
        bool win,
        bool wheelUp,
        ReadOnlySpan<string> pressedMouseButtonsSorted)
    {
        if (!chord.TryGetProperty("kind", out var kind) || !string.Equals(kind.GetString(), "mouseWheel", StringComparison.Ordinal))
            return false;
        if (!chord.TryGetProperty("wheel", out var w))
            return false;
        var wantUp = string.Equals(w.GetString(), "Up", StringComparison.Ordinal);
        if (wantUp != wheelUp)
            return false;
        if (!ModifiersMatch(chord, shift, control, alt, win))
            return false;

        if (!TryGetChordHeldButtonsSorted(chord, out var required) || required.Length == 0)
            return true;

        if (required.Length != pressedMouseButtonsSorted.Length)
            return false;
        for (var i = 0; i < required.Length; i++)
        {
            if (!string.Equals(required[i], pressedMouseButtonsSorted[i], StringComparison.Ordinal))
                return false;
        }

        return true;
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

    /// <param name="pressedMouseButtonsSorted">Sorted unique schema button names currently down.</param>
    public static bool IsMouseChordMatch(
        JsonElement chord,
        bool shift,
        bool control,
        bool alt,
        bool win,
        ReadOnlySpan<string> pressedMouseButtonsSorted)
    {
        if (!chord.TryGetProperty("kind", out var k) || !string.Equals(k.GetString(), "mouseChord", StringComparison.Ordinal))
            return false;
        if (!TryGetChordButtonsSorted(chord, out var required) || required.Length == 0)
            return false;
        if (!ModifiersMatch(chord, shift, control, alt, win))
            return false;
        if (required.Length != pressedMouseButtonsSorted.Length)
            return false;
        for (var i = 0; i < required.Length; i++)
        {
            if (!string.Equals(required[i], pressedMouseButtonsSorted[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool TryGetChordHeldButtonsSorted(JsonElement chord, out string[] sorted)
    {
        sorted = Array.Empty<string>();
        if (!chord.TryGetProperty("heldButtons", out var hb) || hb.ValueKind != JsonValueKind.Array)
            return false;

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in hb.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrEmpty(s))
                set.Add(s);
        }

        if (set.Count == 0)
            return false;

        sorted = set.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool TryGetChordButtonsSorted(JsonElement chord, out string[] sorted)
    {
        sorted = Array.Empty<string>();
        if (!chord.TryGetProperty("buttons", out var bt) || bt.ValueKind != JsonValueKind.Array)
            return false;

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in bt.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrEmpty(s))
                set.Add(s);
        }

        if (set.Count == 0)
            return false;

        sorted = set.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return true;
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

    /// <summary>Enumerates (commandId, chord) for mouseChord bindings in profile order.</summary>
    public static IEnumerable<(string CommandId, JsonElement Chord)> EnumerateMouseChordBindings(InputProfileDocument doc)
    {
        if (doc.Bindings == null)
            yield break;
        foreach (var kv in doc.Bindings)
        {
            foreach (var chord in kv.Value)
            {
                if (chord.TryGetProperty("kind", out var k)
                    && string.Equals(k.GetString(), "mouseChord", StringComparison.Ordinal))
                    yield return (kv.Key, chord);
            }
        }
    }
}
