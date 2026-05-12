namespace ImageHoard.Core.Sort;

/// <summary>Input resolution for sort flag commands (repeat Keep/Delete clears).</summary>
public static class SortFlagInput
{
    public static SortFlagState ResolveToggle(SortFlagState current, SortFlagState requested)
    {
        if (requested == SortFlagState.Unset)
            return SortFlagState.Unset;
        if (requested == SortFlagState.Keep && current == SortFlagState.Keep)
            return SortFlagState.Unset;
        if (requested == SortFlagState.Delete && current == SortFlagState.Delete)
            return SortFlagState.Unset;
        return requested;
    }
}
