namespace Gheychi.Core.Spam;

public interface ISpamSettings
{
    /// <summary>Messages scoring at or above this probability (0..1) are treated as spam.</summary>
    float Threshold { get; set; }

    /// <summary>Quarantined spam older than this many days is deleted.</summary>
    int RetentionDays { get; set; }
}
