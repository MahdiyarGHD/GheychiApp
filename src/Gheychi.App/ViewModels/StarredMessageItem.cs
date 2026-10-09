namespace Gheychi.App.ViewModels;

/// <summary>A starred message in the profile's list. <see cref="Row"/> is its offset from the newest message of the chat.</summary>
public sealed class StarredMessageItem
{
    public required long MessageId { get; init; }
    public required int Row { get; init; }
    public required string Preview { get; init; }
    public required string Time { get; init; }
}
