namespace Gheychi.Core.Spam;

public interface ISpamStatsRepository
{
    Task AddAsync(SpamStat stat);

    /// <summary>Adds the ones whose spam message is not logged yet.</summary>
    Task AddMissingAsync(IReadOnlyCollection<SpamStat> stats);

    Task MarkRestoredAsync(long spamMessageId);

    Task<IReadOnlyList<SpamStat>> GetAllAsync();
}
