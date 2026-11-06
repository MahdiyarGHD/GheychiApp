using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Gheychi.App.ViewModels;

/// <summary>One flat template per row kind. A scrolled-out row is reused for the next row of the same kind instead of rebuilt.</summary>
public sealed class SpamTemplateSelector : IDataTemplate, IRecyclingDataTemplate
{
    private static readonly object DateSeparatorKind = new();
    private static readonly object SpamKind = new();
    private static readonly object BatchKind = new();

    public IDataTemplate? DateSeparatorTemplate { get; set; }
    public IDataTemplate? SpamTemplate { get; set; }
    public IDataTemplate? BatchTemplate { get; set; }

    public bool Match(object? data) => data is DateSeparatorItem or SpamItem or SpamBatchItem;

    public Control? Build(object? data) => Build(data, null);

    public Control? Build(object? data, Control? existing)
    {
        var (kind, template) = data switch
        {
            DateSeparatorItem => (DateSeparatorKind, DateSeparatorTemplate),
            SpamBatchItem => (BatchKind, BatchTemplate),
            _ => (SpamKind, SpamTemplate)
        };
        if (existing is not null && ReferenceEquals(existing.Tag, kind))
            return existing;

        var control = (template ?? throw new InvalidOperationException("Missing row template")).Build(data);
        if (control is not null)
            control.Tag = kind;
        return control;
    }
}
