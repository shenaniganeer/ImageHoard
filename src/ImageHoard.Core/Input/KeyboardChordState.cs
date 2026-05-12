namespace ImageHoard.Core.Input;

/// <summary>Normalized modifier + primary key (MDN-style code) for matching profile chords.</summary>
public readonly record struct KeyboardChordState(
    bool Control,
    bool Shift,
    bool Alt,
    bool Win,
    string PrimaryKey);
