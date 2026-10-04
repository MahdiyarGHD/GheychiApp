namespace Gheychi.App.ViewModels;

public enum ComposeRowKind
{
    Header,
    Contact,
    /// <summary>"Send to 0903…": the number typed in the To field.</summary>
    Typed
}

public sealed class ComposeRow
{
    public required ComposeRowKind Kind { get; init; }

    /// <summary>Section letter for headers.</summary>
    public string Letter { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string Initials { get; init; } = string.Empty;

    /// <summary>Number to send to (contacts and typed rows).</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>Contact name for contact rows, null for a typed number.</summary>
    public string? ContactName { get; init; }
}

public sealed record ComposeRecipient(string Address, string? ContactName, Gheychi.Core.Models.SimCardInfo? Sim);

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
