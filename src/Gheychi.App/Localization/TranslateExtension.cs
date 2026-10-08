namespace Gheychi.App.Localization;

/// <summary><c>{loc:Translate Key}</c>: the text in the language in use. The language only changes with a restart.</summary>
public sealed class TranslateExtension
{
    public TranslateExtension()
    {
    }

    public TranslateExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider? serviceProvider = null) => LocalizationManager.Instance[Key];
}
