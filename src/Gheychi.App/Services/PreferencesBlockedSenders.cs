using Gheychi.Core.Services;

namespace Gheychi.App.Services;

public sealed class PreferencesBlockedSenders : IBlockedSenders
{
    private readonly PreferencesSenderKeys _keys = new("blocked_senders_v1");

    public event EventHandler? Changed;

    public bool IsBlocked(string address) => _keys.Contains(address);

    public IReadOnlyList<string> GetAll() => _keys.GetAll();

    public void SetBlocked(string address, bool blocked)
    {
        if (_keys.Set(address, blocked))
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
