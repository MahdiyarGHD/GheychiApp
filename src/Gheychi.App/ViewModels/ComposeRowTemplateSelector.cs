using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Gheychi.App.ViewModels;

/// <summary>
/// Picks the row template by row kind. A recycled container hands its old child back; it is only reused when that
/// child came from the same template, otherwise a section title would be reused as a contact row.
/// </summary>
public sealed class ComposeRowTemplateSelector : IRecyclingDataTemplate
{
    /// <summary>The empty space the list ends with; it scrolls with the rows, as the footer of the MAUI list did.</summary>
    public static readonly ComposeRow Footer = new() { Kind = ComposeRowKind.Header };

    public IDataTemplate? Header { get; set; }
    public IDataTemplate? Contact { get; set; }
    public IDataTemplate? ContactIcon { get; set; }
    public IDataTemplate? Typed { get; set; }
    public IDataTemplate? FooterSpace { get; set; }

    public bool Match(object? data) => data is ComposeRow;

    public Control? Build(object? data) => Build(data, null);

    public Control? Build(object? data, Control? existing)
    {
        var template = Select(data);
        if (template is null)
            return null;

        if (existing is not null && ReferenceEquals(existing.Tag, template) && template is IRecyclingDataTemplate recycling)
            return recycling.Build(data, existing);

        var built = template.Build(data);
        if (built is not null)
            built.Tag = template;
        return built;
    }

    private IDataTemplate? Select(object? data) =>
        data is not ComposeRow row
            ? null
            : ReferenceEquals(row, Footer)
                ? FooterSpace
                : row.Kind switch
                {
                    ComposeRowKind.Header => Header,
                    ComposeRowKind.Typed => Typed,
                    _ => row.HasInitials ? Contact : ContactIcon
                };
}
