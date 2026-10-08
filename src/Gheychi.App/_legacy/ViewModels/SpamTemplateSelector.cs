namespace Gheychi.App.ViewModels;

public sealed class SpamTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DateSeparatorTemplate { get; set; }
    public DataTemplate? SpamTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        item is DateSeparatorItem
            ? DateSeparatorTemplate ?? throw new InvalidOperationException(nameof(DateSeparatorTemplate))
            : SpamTemplate ?? throw new InvalidOperationException(nameof(SpamTemplate));
}
