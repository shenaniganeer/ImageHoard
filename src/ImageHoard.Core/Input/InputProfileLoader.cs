using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageHoard.Core.Input;

public sealed class InputProfileDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("bindings")]
    public Dictionary<string, List<JsonElement>>? Bindings { get; set; }
}

public static class InputProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static InputProfileDocument LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<InputProfileDocument>(json, JsonOptions)
                  ?? throw new InvalidDataException("Empty profile");
        return doc;
    }
}

/// <summary>FR-IN-05 — detect duplicate chords targeting different commands.</summary>
public static class InputBindingConflictChecker
{
    public static IReadOnlyList<string> FindChordKeyConflicts(InputProfileDocument profile)
    {
        var issues = new List<string>();
        if (profile.Bindings == null)
            return issues;

        var chordInstances = new Dictionary<string, List<(string CommandId, InputBindingActivationMask Mask)>>(
            StringComparer.Ordinal);

        foreach (var (commandId, chords) in profile.Bindings)
        {
            foreach (var chord in chords)
            {
                if (!chord.TryGetProperty("kind", out var kindProp))
                    continue;
                var kind = kindProp.GetString() ?? "";
                var fingerprint = Fingerprint(kind, chord);
                if (fingerprint.Length == 0)
                    continue;

                var mask = InputBindingActivation.ResolveMask(commandId, chord);
                if (!chordInstances.TryGetValue(fingerprint, out var list))
                {
                    list = new List<(string, InputBindingActivationMask)>();
                    chordInstances[fingerprint] = list;
                }

                list.Add((commandId, mask));
            }
        }

        foreach (var (fp, instances) in chordInstances)
        {
            var cmds = instances.Select(i => i.CommandId).Distinct(StringComparer.Ordinal).ToList();
            if (cmds.Count < 2)
                continue;

            var conflict = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < cmds.Count; i++)
            {
                for (var j = i + 1; j < cmds.Count; j++)
                {
                    var a = cmds[i];
                    var b = cmds[j];
                    var masksA = instances.Where(t => t.CommandId == a).Select(t => t.Mask).Distinct().ToList();
                    var masksB = instances.Where(t => t.CommandId == b).Select(t => t.Mask).Distinct().ToList();
                    foreach (var mA in masksA)
                    {
                        foreach (var mB in masksB)
                        {
                            if ((mA & mB) == 0)
                                continue;
                            if (fp.StartsWith("keyboard:", StringComparison.Ordinal)
                                && mA == InputBindingActivationMask.Browse
                                && mB == InputBindingActivationMask.Browse
                                && IsExemptNavVersusBrowserTreeKeyboardPair(a, b))
                                continue;

                            conflict.Add(a);
                            conflict.Add(b);
                        }
                    }
                }
            }

            if (conflict.Count < 2)
                continue;

            var ordered = conflict.OrderBy(s => s, StringComparer.Ordinal).ToList();
            issues.Add($"Chord '{fp}' maps to multiple commands: {string.Join(", ", ordered)}");
        }

        return issues;
    }

    private static readonly HashSet<string> NavVersusTreeExemptNavIds = new(StringComparer.Ordinal)
    {
        "nav.nextImage",
        "nav.prevImage",
    };

    /// <summary>Arrow keys overlap between image navigation and tree-scoped commands; disambiguated by focus at dispatch.</summary>
    private static bool IsExemptNavVersusBrowserTreeKeyboardPair(string commandIdA, string commandIdB)
    {
        var aNav = NavVersusTreeExemptNavIds.Contains(commandIdA);
        var bNav = NavVersusTreeExemptNavIds.Contains(commandIdB);
        var aTree = BrowserTreeKeyboardCommandIds.IsTreeCommand(commandIdA);
        var bTree = BrowserTreeKeyboardCommandIds.IsTreeCommand(commandIdB);
        return (aNav && bTree) || (bNav && aTree);
    }

    private static string Fingerprint(string kind, JsonElement chord)
    {
        return kind switch
        {
            "keyboard" => KindKeyboard(chord),
            "mouseButton" => KindMouseButton(chord),
            "mouseWheel" => KindMouseWheel(chord),
            "mouseChord" => KindMouseChord(chord),
            "mouseWheelTilt" => KindMouseWheelTilt(chord),
            _ => string.Empty,
        };
    }

    private static string KindKeyboard(JsonElement chord)
    {
        if (!chord.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = keys.EnumerateArray().Select(k => k.GetString() ?? "").Where(s => s.Length > 0).ToArray();
        Array.Sort(parts, StringComparer.Ordinal);
        return "keyboard:" + string.Join("+", parts);
    }

    private static string KindMouseButton(JsonElement chord)
    {
        if (!chord.TryGetProperty("button", out var b))
            return string.Empty;
        var click = chord.TryGetProperty("clickCount", out var cc) ? cc.GetInt32() : 1;
        var mods = ModifierFingerprint(chord);
        return $"mouseButton:{b.GetString()}:{click}:{mods}";
    }

    private static string KindMouseWheel(JsonElement chord)
    {
        if (!chord.TryGetProperty("wheel", out var w))
            return string.Empty;
        var mods = ModifierFingerprint(chord);
        var held = HeldButtonsFingerprint(chord);
        return held.Length > 0
            ? "mouseWheel:" + w.GetString() + ":" + mods + ":held:" + held
            : "mouseWheel:" + w.GetString() + ":" + mods;
    }

    /// <summary>Non-empty sorted unique <c>heldButtons</c> for <c>mouseWheel</c> chords; empty string if absent or empty.</summary>
    private static string HeldButtonsFingerprint(JsonElement chord)
    {
        if (!chord.TryGetProperty("heldButtons", out var hb) || hb.ValueKind != JsonValueKind.Array)
            return "";
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in hb.EnumerateArray())
        {
            var s = b.GetString();
            if (!string.IsNullOrEmpty(s))
                set.Add(s);
        }

        if (set.Count == 0)
            return "";
        var parts = set.ToArray();
        Array.Sort(parts, StringComparer.Ordinal);
        return string.Join("+", parts);
    }

    private static string KindMouseChord(JsonElement chord)
    {
        if (!chord.TryGetProperty("buttons", out var buttons) || buttons.ValueKind != JsonValueKind.Array)
            return string.Empty;
        var parts = buttons.EnumerateArray().Select(b => b.GetString() ?? "").Where(s => s.Length > 0).ToArray();
        Array.Sort(parts, StringComparer.Ordinal);
        var mods = ModifierFingerprint(chord);
        return "mouseChord:" + string.Join("+", parts) + ":" + mods;
    }

    private static string KindMouseWheelTilt(JsonElement chord)
    {
        if (!chord.TryGetProperty("tilt", out var t))
            return string.Empty;
        var mods = ModifierFingerprint(chord);
        return "mouseWheelTilt:" + t.GetString() + ":" + mods;
    }

    private static string ModifierFingerprint(JsonElement chord)
    {
        if (!chord.TryGetProperty("modifiers", out var mods) || mods.ValueKind != JsonValueKind.Array)
            return "";
        var parts = mods.EnumerateArray().Select(m => m.GetString() ?? "").Where(s => s.Length > 0).ToArray();
        Array.Sort(parts, StringComparer.Ordinal);
        return string.Join("+", parts);
    }
}
