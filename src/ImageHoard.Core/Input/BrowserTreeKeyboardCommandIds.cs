using System.Collections.Generic;

namespace ImageHoard.Core.Input;

/// <summary>Keyboard commands dispatched only while focus is inside the browser folder tree.</summary>
public static class BrowserTreeKeyboardCommandIds
{
    public const string TreeNext = "browse.treeNext";
    public const string TreePrevious = "browse.treePrevious";
    public const string TreeExpand = "browse.treeExpand";
    public const string TreeCollapse = "browse.treeCollapse";

    /// <summary>Stable order when building the tree-only dispatch table (first matching chord wins).</summary>
    public static readonly string[] InDispatchOrder =
    {
        TreeCollapse,
        TreeExpand,
        TreeNext,
        TreePrevious,
    };

    public static bool IsTreeCommand(string commandId) =>
        commandId is TreeNext or TreePrevious or TreeExpand or TreeCollapse;

    /// <summary>Commands excluded from the global keyboard table (handled via a tree-scoped table).</summary>
    public static readonly IReadOnlySet<string> AllTreeCommandIdSet = new HashSet<string>(StringComparer.Ordinal)
    {
        TreeNext,
        TreePrevious,
        TreeExpand,
        TreeCollapse,
    };
}
