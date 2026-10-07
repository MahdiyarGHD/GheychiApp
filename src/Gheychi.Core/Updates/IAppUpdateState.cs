namespace Gheychi.Core.Updates;

/// <summary>What the last app update check found, kept across restarts.</summary>
public interface IAppUpdateState
{
    DateTime? LastCheckedUtc { get; set; }

    AppRelease? Available { get; set; }
}
