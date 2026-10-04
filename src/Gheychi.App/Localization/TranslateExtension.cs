namespace Gheychi.App.Localization;

[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public object ProvideValue(IServiceProvider serviceProvider) =>
        LocalizationManager.Instance[Key];
}
