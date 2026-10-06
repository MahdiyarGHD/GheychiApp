namespace Gheychi.Core.Spam;

/// <summary>Senders the user made immune to the spam check, like a saved contact.</summary>
public interface ITrustedSenders
{
    bool IsTrusted(string address);

    void SetTrusted(string address, bool trusted);
}
