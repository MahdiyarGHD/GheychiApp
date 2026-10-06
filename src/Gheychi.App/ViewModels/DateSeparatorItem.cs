namespace Gheychi.App.ViewModels;

public sealed record DateSeparatorItem(string Text)
{
    private static readonly Color SepBgDark = Color.FromArgb("#1C1F24");
    private static readonly Color SepBgLight = Color.FromArgb("#E8EDE6");
    private static readonly Color SepText = Color.FromArgb("#8A8F98");

    public static Color Background =>
        Application.Current?.RequestedTheme == AppTheme.Dark ? SepBgDark : SepBgLight;

    public static Color TextColor => SepText;
}
