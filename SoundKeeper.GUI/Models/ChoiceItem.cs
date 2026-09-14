namespace SoundKeeper.GUI.Models;

public sealed record ChoiceItem<T>(T Value, string Label, string Description = "") where T : struct, Enum;
