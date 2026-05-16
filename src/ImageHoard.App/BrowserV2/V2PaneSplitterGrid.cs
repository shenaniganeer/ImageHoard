using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace ImageHoard.App.BrowserV2;

/// <summary>6px strip between Browse2 folder and image panes (resize cursor only; drag handled on <see cref="BrowserV2Host"/>).</summary>
public sealed class V2PaneSplitterGrid : Grid
{
    public V2PaneSplitterGrid() =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
