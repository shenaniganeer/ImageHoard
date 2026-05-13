using Microsoft.UI.Input;

namespace ImageHoard.App;

/// <summary>Maps WinUI <see cref="PointerPointProperties"/> to schema mouse button names for wheel chord matching.</summary>
/// <remarks>
/// WinUI exposes Left, Middle, Right, X1, and X2; X3+ may appear in hand-edited JSON but are not read from pointer state.
/// </remarks>
internal static class PointerInputMouseHeldButtons
{
    internal static string[] GetPressedSorted(PointerPointProperties p)
    {
        var list = new List<string>(5);
        if (p.IsLeftButtonPressed)
            list.Add("Left");
        if (p.IsMiddleButtonPressed)
            list.Add("Middle");
        if (p.IsRightButtonPressed)
            list.Add("Right");
        if (p.IsXButton1Pressed)
            list.Add("X1");
        if (p.IsXButton2Pressed)
            list.Add("X2");
        list.Sort(StringComparer.Ordinal);
        return list.ToArray();
    }
}
