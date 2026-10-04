using Gheychi.App.Localization;

namespace Gheychi.App.ViewModels;

public sealed class SpamViewModel
{
    public IReadOnlyList<SpamItem> Items { get; } = new List<SpamItem>
    {
        new()
        {
            Sender = "QuickLoans",
            Category = "Unsolicited loan",
            Time = "10:14 AM",
            Preview = "Congratulations! Pre-approved instant cash up to $15,000 with 0% interest for...",
            IconFile = "spamrow.png"
        },
        new()
        {
            Sender = "+1 (800) 492...",
            Category = "Suspicious link",
            Time = LocalizationManager.Instance["Time_Yesterday"],
            Preview = "Parcel #US-98219 delivery suspended due to incomplete address verification....",
            IconFile = "spamhidden.png"
        },
        new()
        {
            Sender = "CryptoExpress",
            Category = "Keyword rule",
            Time = "Oct 24",
            Preview = "Wallet Alert: You received 0.428 ETH from an anonymous smart contract....",
            IconFile = "bitcoin.png"
        }
    };
}
