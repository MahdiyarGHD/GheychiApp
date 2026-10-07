namespace Gheychi.Core.Spam;

/// <summary>What the last model update check found, kept across restarts.</summary>
public interface ISpamModelUpdateState
{
    DateTime? LastCheckedUtc { get; set; }

    SpamModelRelease? Available { get; set; }
}
