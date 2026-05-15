using System.Text.Json;

namespace ImageHoard.Core.Input;

/// <summary>Where a binding may activate: browse UI, slideshow UI, or both (global).</summary>
[Flags]
public enum InputBindingActivationMask
{
    None = 0,
    Browse = 1 << 0,
    Slideshow = 1 << 1,
    /// <summary>Active in both browse and slideshow UI.</summary>
    Global = Browse | Slideshow,
}

/// <summary>Resolves per-chord activation for dispatch and FR-IN-05 conflict detection.</summary>
public static class InputBindingActivation
{
    /// <summary>Optional JSON property on a chord object: <c>browse</c>, <c>slideshow</c>, or <c>global</c>.</summary>
    public const string ActivationJsonPropertyName = "activation";

    /// <summary>
    /// True when <paramref name="mask"/> applies while slideshow UI is <paramref name="slideshowUiActive"/>.
    /// </summary>
    public static bool MaskAppliesToUi(InputBindingActivationMask mask, bool slideshowUiActive) =>
        slideshowUiActive
            ? (mask & InputBindingActivationMask.Slideshow) != 0
            : (mask & InputBindingActivationMask.Browse) != 0;

    /// <summary>Mask from optional chord property, else inferred from <paramref name="commandId"/>.</summary>
    public static InputBindingActivationMask ResolveMask(string commandId, JsonElement chord)
    {
        if (chord.TryGetProperty(ActivationJsonPropertyName, out var act))
        {
            var s = act.GetString();
            if (!string.IsNullOrEmpty(s))
            {
                if (string.Equals(s, "browse", StringComparison.OrdinalIgnoreCase))
                    return InputBindingActivationMask.Browse;
                if (string.Equals(s, "slideshow", StringComparison.OrdinalIgnoreCase))
                    return InputBindingActivationMask.Slideshow;
                if (string.Equals(s, "global", StringComparison.OrdinalIgnoreCase))
                    return InputBindingActivationMask.Global;
            }
        }

        return InferMaskForCommand(commandId);
    }

    /// <summary>Inferred activation when the chord omits <c>activation</c>.</summary>
    public static InputBindingActivationMask InferMaskForCommand(string commandId)
    {
        if (string.Equals(commandId, "slideshow.start", StringComparison.Ordinal))
            return InputBindingActivationMask.Browse;

        if (commandId.StartsWith("slideshow.", StringComparison.Ordinal))
            return InputBindingActivationMask.Slideshow;

        if (commandId.StartsWith("sort.", StringComparison.Ordinal)
            || commandId.StartsWith("browse.", StringComparison.Ordinal)
            || commandId.StartsWith("nav.", StringComparison.Ordinal))
            return InputBindingActivationMask.Browse;

        if (commandId.StartsWith("view.", StringComparison.Ordinal)
            || commandId.StartsWith("settings.", StringComparison.Ordinal)
            || commandId.StartsWith("ui.", StringComparison.Ordinal))
            return InputBindingActivationMask.Global;

        return InputBindingActivationMask.Global;
    }
}
