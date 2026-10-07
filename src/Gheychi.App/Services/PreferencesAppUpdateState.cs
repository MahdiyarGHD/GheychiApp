using Gheychi.Core.Updates;

namespace Gheychi.App.Services;

public sealed class PreferencesAppUpdateState : IAppUpdateState
{
    private const string LastCheckedKey = "app_update_last_checked_v1";
    private const string AvailableTagKey = "app_update_available_tag_v1";
    private const string AvailablePageKey = "app_update_available_page_v1";

    public DateTime? LastCheckedUtc
    {
        get => Preferences.Default.ContainsKey(LastCheckedKey)
            ? DateTime.SpecifyKind(Preferences.Default.Get(LastCheckedKey, DateTime.MinValue), DateTimeKind.Utc)
            : null;
        set
        {
            if (value is { } time)
                Preferences.Default.Set(LastCheckedKey, time);
            else
                Preferences.Default.Remove(LastCheckedKey);
        }
    }

    public AppRelease? Available
    {
        get
        {
            var tag = Preferences.Default.Get(AvailableTagKey, string.Empty);
            var page = Preferences.Default.Get(AvailablePageKey, string.Empty);
            return tag.Length == 0 || page.Length == 0 ? null : new AppRelease(tag, page);
        }
        set
        {
            if (value is null)
            {
                Preferences.Default.Remove(AvailableTagKey);
                Preferences.Default.Remove(AvailablePageKey);
            }
            else
            {
                Preferences.Default.Set(AvailableTagKey, value.Tag);
                Preferences.Default.Set(AvailablePageKey, value.PageUrl);
            }
        }
    }
}
