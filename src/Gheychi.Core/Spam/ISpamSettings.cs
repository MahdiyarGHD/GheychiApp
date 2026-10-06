namespace Gheychi.Core.Spam;

public interface ISpamSettings
{
    /// <summary>When off, no message is checked and everything goes to the inbox.</summary>
    bool Enabled { get; set; }

    /// <summary>Messages scoring at or above this probability (0..1) are treated as spam.</summary>
    float Threshold { get; set; }

    /// <summary>Quarantined spam older than this many days is deleted.</summary>
    int RetentionDays { get; set; }
}
