namespace Gheychi.App.Theming;

/// <summary>
/// How big the text of a conversation is. Like the theme and the accent it is fixed for the life of the process
/// (changing it restarts the app), so the sizes are plain numbers put in the app's resources once.
/// </summary>
public static class TextScale
{
    public const int DefaultStep = 1;

    public static readonly double[] Steps = [0.9, 1, 1.12, 1.25];

    public static double Current { get; private set; } = Steps[DefaultStep];

    public static void Resolve()
    {
        var step = Services.AppPreferences.TextSize;
        Current = Steps[step >= 0 && step < Steps.Length ? step : DefaultStep];
    }

    public static double Body => Scaled(14.5);

    public static double Meta => Scaled(11.5);

    public static double Reaction => Scaled(13);

    private static double Scaled(double size) => Math.Round(size * Current * 2) / 2;
}
