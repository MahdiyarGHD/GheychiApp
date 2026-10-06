using Gheychi.Core.Spam;

namespace Gheychi.App.ViewModels;

public sealed class SpamItem
{
    public required SpamMessage Message { get; init; }
    public long Id => Message.Id;
    public required string Sender { get; init; }
    public required string Confidence { get; init; }
    public required string Time { get; init; }
    public string Preview => Message.Body;
}
