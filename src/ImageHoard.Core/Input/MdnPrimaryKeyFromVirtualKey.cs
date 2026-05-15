namespace ImageHoard.Core.Input;

/// <summary>Maps Windows virtual-key values to MDN-style <c>KeyboardEvent.code</c> tokens for profile chords.</summary>
public static class MdnPrimaryKeyFromVirtualKey
{
    // Windows.System.VirtualKey (subset used for zoom / numpad operators)
    private const int VkOemPlus = 0xBB;
    private const int VkOemMinus = 0xBD;
    private const int VkAdd = 0x6B;
    private const int VkSubtract = 0x6D;

    /// <summary>Returns an MDN primary key for OEM and numpad keys, or null when unknown.</summary>
    public static string? TryOemAndNumpad(int virtualKey) =>
        virtualKey switch
        {
            VkOemPlus => "Equal",
            VkOemMinus => "Minus",
            VkAdd => "NumpadAdd",
            VkSubtract => "NumpadSubtract",
            _ => null,
        };
}
