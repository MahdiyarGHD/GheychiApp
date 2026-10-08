using Avalonia.Media;

namespace Gheychi.App.ViewModels;

public sealed record DateSeparatorItem(string Text)
{
    public static IBrush Background { get; } = Palette.Pick("#E8EDE6", "#1C1F24");

    public static IBrush TextColor { get; } = Palette.Brush("#8A8F98");
}
