using System.Text.Json;
using ImageHoard.Core.Input;

namespace ImageHoard.App;

internal static class InputProfileBootstrap
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads shipped KeyboardOnly + MouseOnly profiles from <c>defaults/input-profiles/index.json</c> and merges bindings.
    /// If only one profile file is present, returns that document (cloned shallow with combined metadata when merging is trivial).
    /// </summary>
    public static InputProfileDocument? TryLoadCombinedShippedBuiltin(string? baseDirectory = null)
    {
        var baseDir = baseDirectory ?? AppContext.BaseDirectory;
        var indexPath = Path.Combine(baseDir, "defaults", "input-profiles", "index.json");
        if (!File.Exists(indexPath))
            return null;

        try
        {
            var index = JsonSerializer.Deserialize<InputProfileIndexDocument>(File.ReadAllText(indexPath), JsonOptions);
            if (index?.BuiltinProfiles == null || index.BuiltinProfiles.Count == 0)
                return null;

            var kbRel = index.BuiltinProfiles.FirstOrDefault(p =>
                string.Equals(p.ProfileId, "KeyboardOnly", StringComparison.OrdinalIgnoreCase))?.Path;
            var mouseRel = index.BuiltinProfiles.FirstOrDefault(p =>
                string.Equals(p.ProfileId, "MouseOnly", StringComparison.OrdinalIgnoreCase))?.Path;

            InputProfileDocument? kb = null;
            InputProfileDocument? mouse = null;

            if (!string.IsNullOrEmpty(kbRel))
            {
                var p = Path.Combine(baseDir, "defaults", "input-profiles", kbRel);
                if (File.Exists(p))
                    kb = InputProfileLoader.LoadFromFile(p);
            }

            if (!string.IsNullOrEmpty(mouseRel))
            {
                var p = Path.Combine(baseDir, "defaults", "input-profiles", mouseRel);
                if (File.Exists(p))
                    mouse = InputProfileLoader.LoadFromFile(p);
            }

            if (kb != null && mouse != null)
                return InputProfileMerger.MergeBindingLists(kb, mouse);
            if (kb != null)
            {
                var one = InputProfileMerger.CloneShallow(kb);
                one.ProfileId = "Combined";
                one.DisplayName = "Keyboard and mouse defaults";
                return one;
            }

            if (mouse != null)
            {
                var one = InputProfileMerger.CloneShallow(mouse);
                one.ProfileId = "Combined";
                one.DisplayName = "Keyboard and mouse defaults";
                return one;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
