namespace Gheychi.App.ViewModels;

public sealed class SpamItem
{
    public long Id { get; init; }
    public required string Sender { get; init; }
    public required string Time { get; init; }
    public required string Preview { get; init; }
}
