namespace ImageHoard.Core.Input;

/// <summary>Matches keyboard chords from input profiles (FR-IN-01).</summary>
public static class KeyboardChordMatcher
{
    private static readonly HashSet<string> ModifierTokens = new(StringComparer.Ordinal)
    {
        "Control", "Shift", "Alt", "Win",
    };

    /// <summary>
    /// Returns true when <paramref name="keys"/> matches the chord state.
    /// Expects modifiers first, then a single non-modifier key per keyboard-key-identifiers.md.
    /// </summary>
    public static bool Matches(IReadOnlyList<string> keys, in KeyboardChordState state)
    {
        if (keys.Count == 0)
            return false;

        var wantCtrl = false;
        var wantShift = false;
        var wantAlt = false;
        var wantWin = false;
        string? main = null;
        foreach (var raw in keys)
        {
            var token = raw.Trim();
            if (token.Length == 0)
                continue;
            if (ModifierTokens.Contains(token))
            {
                switch (token)
                {
                    case "Control": wantCtrl = true; break;
                    case "Shift": wantShift = true; break;
                    case "Alt": wantAlt = true; break;
                    case "Win": wantWin = true; break;
                }
            }
            else
            {
                main = token;
            }
        }

        if (string.IsNullOrEmpty(main))
            return false;

        return wantCtrl == state.Control
               && wantShift == state.Shift
               && wantAlt == state.Alt
               && wantWin == state.Win
               && string.Equals(main, state.PrimaryKey, StringComparison.Ordinal);
    }
}
