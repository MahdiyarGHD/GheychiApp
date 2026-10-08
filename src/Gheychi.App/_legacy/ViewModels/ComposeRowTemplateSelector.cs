namespace Gheychi.App.ViewModels;

public sealed class ComposeRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? ContactTemplate { get; set; }
    public DataTemplate? TypedTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        (item as ComposeRow)?.Kind switch
        {
            ComposeRowKind.Header => HeaderTemplate ?? throw new InvalidOperationException(nameof(HeaderTemplate)),
            ComposeRowKind.Typed => TypedTemplate ?? throw new InvalidOperationException(nameof(TypedTemplate)),
            _ => ContactTemplate ?? throw new InvalidOperationException(nameof(ContactTemplate))
        };
}
