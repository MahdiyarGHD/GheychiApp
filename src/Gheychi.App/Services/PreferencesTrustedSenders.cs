using Gheychi.Core.Spam;

namespace Gheychi.App.Services;

public sealed class PreferencesTrustedSenders : ITrustedSenders
{
    private readonly PreferencesSenderKeys _keys = new("spam_trusted_senders_v1");

    public bool IsTrusted(string address) => _keys.Contains(address);

    public IReadOnlyList<string> GetAll() => _keys.GetAll();

    public void SetTrusted(string address, bool trusted) => _keys.Set(address, trusted);
}
