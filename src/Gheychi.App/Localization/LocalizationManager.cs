using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Gheychi.App.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private static readonly ResourceManager ResourceManager =
        new("Gheychi.App.Localization.AppResources", typeof(LocalizationManager).Assembly);

    private LocalizationManager()
    {
    }

    public string this[string key] =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaiseChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
