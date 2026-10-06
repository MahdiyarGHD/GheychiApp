using Gheychi.Core.Services;

namespace Gheychi.Core.Spam;

public static class SpamStats
{
    public static SpamStat From(SpamMessage message) =>
        new(0, message.Id, PhoneNumberNormalizer.ToLookupKey(message.Address), message.Timestamp, message.Score, message.ModelVersion);
}
