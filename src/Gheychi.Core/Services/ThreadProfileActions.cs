namespace Gheychi.Core.Services;

/// <summary>
/// Which of the profile's quick buttons make sense for a conversation. Each button has its own rule,
/// so a conversation shows only what it can do and the remaining buttons share the width.
/// </summary>
public sealed record ThreadProfileActions(bool Call, bool Text, bool Details, bool Search)
{
    public static ThreadProfileActions For(string? address) => new(
        Call: CanCall(address),
        Text: CanReply(address),
        Details: true,
        Search: true);

    /// <summary>Only a real phone number can be dialled; sender names and operator shortcodes cannot.</summary>
    public static bool CanCall(string? address) => ContactListBuilder.IsPersonalNumber(address);

    /// <summary>Sender names (IRANCELL, MCI-NET) have no return route, so a reply to one goes nowhere; short codes can answer.</summary>
    public static bool CanReply(string? address) =>
        !string.IsNullOrWhiteSpace(address) && !PhoneNumberNormalizer.IsAlphanumeric(address);

    public int VisibleCount => (Call ? 1 : 0) + (Text ? 1 : 0) + (Details ? 1 : 0) + (Search ? 1 : 0);
}
