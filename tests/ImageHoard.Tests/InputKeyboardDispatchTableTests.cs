using System.Collections.Generic;
using System.Text.Json;
using ImageHoard.Core.Input;

namespace ImageHoard.Tests;

public sealed class InputKeyboardDispatchTableTests
{
    [Fact]
    public void FromProfileExcludingCommandIds_removes_rows_for_excluded_commands()
    {
        var doc = new InputProfileDocument { Bindings = new Dictionary<string, List<JsonElement>>() };
        var arrowDown = JsonSerializer.Deserialize<JsonElement>("""{"kind":"keyboard","keys":["ArrowDown"]}""")!;
        doc.Bindings["nav.nextImage"] = new List<JsonElement> { arrowDown };
        doc.Bindings["browse.treeNext"] = new List<JsonElement> { JsonSerializer.Deserialize<JsonElement>(arrowDown.GetRawText())! };

        var main = InputKeyboardDispatchTable.FromProfileExcludingCommandIds(
            doc,
            BrowserTreeKeyboardCommandIds.AllTreeCommandIdSet);
        var state = new KeyboardChordState(false, false, false, false, "ArrowDown");
        Assert.Equal("nav.nextImage", main.TryMatchFirst(state));

        var tree = InputKeyboardDispatchTable.FromProfileIncludingCommandsInOrder(
            doc,
            BrowserTreeKeyboardCommandIds.InDispatchOrder);
        Assert.Equal("browse.treeNext", tree.TryMatchFirst(state));
    }

    [Fact]
    public void FromProfileIncludingCommandsInOrder_prefers_earlier_command_when_chords_overlap()
    {
        var doc = new InputProfileDocument { Bindings = new Dictionary<string, List<JsonElement>>() };
        var arrow = JsonSerializer.Deserialize<JsonElement>("""{"kind":"keyboard","keys":["ArrowDown"]}""")!;
        doc.Bindings["browse.treeNext"] = new List<JsonElement> { arrow };
        doc.Bindings["browse.treePrevious"] = new List<JsonElement> { JsonSerializer.Deserialize<JsonElement>(arrow.GetRawText())! };

        var tree = InputKeyboardDispatchTable.FromProfileIncludingCommandsInOrder(
            doc,
            BrowserTreeKeyboardCommandIds.InDispatchOrder);
        var state = new KeyboardChordState(false, false, false, false, "ArrowDown");
        // InDispatchOrder lists TreeCollapse, TreeExpand, TreeNext, TreePrevious — first with a binding wins.
        Assert.Equal("browse.treeNext", tree.TryMatchFirst(state));
    }
}
