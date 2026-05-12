using ImageHoard.Core.Input;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace ImageHoard.App;

internal static class WinUiKeyboardInterop
{
    public static (bool Control, bool Shift, bool Alt, bool Win) GetModifierStates()
    {
        static bool Down(VirtualKey vk) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(vk).HasFlag(CoreVirtualKeyStates.Down);

        return (Down(VirtualKey.Control), Down(VirtualKey.Shift), Down(VirtualKey.Menu), Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows));
    }

    public static KeyboardChordState GetKeyboardChordState(string primaryMdnKey)
    {
        var (c, s, a, w) = GetModifierStates();
        return new KeyboardChordState(c, s, a, w, primaryMdnKey);
    }

    /// <summary>Maps a WinUI key to MDN-style <c>Key*</c> / <c>Arrow*</c> identifier, or null if not mapped.</summary>
    public static string? ToMdnPrimaryKey(VirtualKey key)
    {
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
            return "Key" + (char)('A' + (key - VirtualKey.A));

        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            return "Digit" + (key - VirtualKey.Number0);

        return key switch
        {
            VirtualKey.Left => "ArrowLeft",
            VirtualKey.Right => "ArrowRight",
            VirtualKey.Up => "ArrowUp",
            VirtualKey.Down => "ArrowDown",
            VirtualKey.Space => "Space",
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Escape",
            VirtualKey.Tab => "Tab",
            VirtualKey.Back => "Backspace",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.Delete => "Delete",
            VirtualKey.F1 => "F1",
            VirtualKey.F2 => "F2",
            VirtualKey.F3 => "F3",
            VirtualKey.F4 => "F4",
            VirtualKey.F5 => "F5",
            VirtualKey.F6 => "F6",
            VirtualKey.F7 => "F7",
            VirtualKey.F8 => "F8",
            VirtualKey.F9 => "F9",
            VirtualKey.F10 => "F10",
            VirtualKey.F11 => "F11",
            VirtualKey.F12 => "F12",
            _ => null,
        };
    }
}
