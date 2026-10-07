namespace Gheychi.Core.Spam;

/// <summary>Where users' corrections of the spam detector are collected, to train the next model.</summary>
public interface ISpamReporter
{
    /// <summary>Sends one SMS with the user's verdict. A text that normalizes to nothing is not sent.</summary>
    /// <exception cref="Exception">The report could not be sent.</exception>
    Task ReportAsync(string text, bool isSpam, CancellationToken cancellationToken = default);
}
