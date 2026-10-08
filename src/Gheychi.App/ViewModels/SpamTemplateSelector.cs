using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Gheychi.App.ViewModels;

/// <summary>One flat template per row kind. A scrolled-out row is reused for the next row of the same kind instead of rebuilt.</summary>
public sealed class SpamTemplateSelector : IDataTemplate, IRecyclingDataTemplate
{
    private static readonly object DateSeparatorKind = new();
    private static readonly object SpamKind = new();

    public IDataTemplate? DateSeparatorTemplate { get; set; }
    public IDataTemplate? SpamTemplate { get; set; }

    public bool Match(object? data) => data is DateSeparatorItem or SpamItem;

    public Control? Build(object? data) => Build(data, null);

    public Control? Build(object? data, Control? existing)
    {
        var kind = data is DateSeparatorItem ? DateSeparatorKind : SpamKind;
        if (existing is not null && ReferenceEquals(existing.Tag, kind))
            return existing;

        var template = data is DateSeparatorItem ? DateSeparatorTemplate : SpamTemplate;
        var control = (template ?? throw new InvalidOperationException("Missing row template")).Build(data);
        if (control is not null)
            control.Tag = kind;
        return control;
    }
}
