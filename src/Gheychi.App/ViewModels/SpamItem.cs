namespace Gheychi.App.ViewModels;

public sealed class SpamItem
{
    public required string Sender { get; init; }
    public required string Category { get; init; }
    public required string Time { get; init; }
    public required string Preview { get; init; }
    public required string IconFile { get; init; }
}
