namespace Gheychi.Core.Services;

public static class SimResolver
{
    /// <summary>
    /// Picks the subscription a reply/reaction to a message must be sent from.
    /// A received message carries the subscription that received it; replying from that
    /// SIM keeps the conversation direction right on a multi-SIM phone. Messages stored
    /// without a subscription (sub_id &lt;= 0) must fall back to the SIM selected in the
    /// chat, never to slot 1 — that silently flipped sender and recipient.
    /// </summary>
    public static int ResolveSendSubId(
        int messageSubId,
        int selectedSubId,
        int selectedSlot,
        IReadOnlyDictionary<int, int>? subToSlot = null)
    {
        if (messageSubId > 0)
            return messageSubId;

        if (selectedSubId > 0)
            return selectedSubId;

        if (selectedSlot > 0 && subToSlot is not null)
        {
            foreach (var (subId, slot) in subToSlot)
            {
                if (slot == selectedSlot && subId > 0)
                    return subId;
            }
        }

        return selectedSlot > 0 ? selectedSlot : 0;
    }
}
