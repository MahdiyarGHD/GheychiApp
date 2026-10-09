using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Controls;

/// <summary>
/// <c>ctrl:SnippetText.Runs="{Binding SnippetRuns}"</c>: fills a TextBlock's inlines from the runs of a search snippet.
/// The runs are rebuilt only when the bound array changes, i.e. when a recycled row gets another item.
/// </summary>
public static class SnippetText
{
    public static readonly AttachedProperty<SnippetRun[]?> RunsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, SnippetRun[]?>("Runs", typeof(SnippetText));

    static SnippetText()
    {
        RunsProperty.Changed.AddClassHandler<TextBlock>(static (block, e) => Apply(block, e.NewValue as SnippetRun[]));
    }

    public static SnippetRun[]? GetRuns(TextBlock block) => block.GetValue(RunsProperty);

    public static void SetRuns(TextBlock block, SnippetRun[]? value) => block.SetValue(RunsProperty, value);

    private static void Apply(TextBlock block, SnippetRun[]? runs)
    {
        var inlines = block.Inlines;
        if (inlines is null)
        {
            inlines = [];
            block.Inlines = inlines;
        }
        else
        {
            inlines.Clear();
        }

        if (runs is null)
            return;

        var built = new Inline[runs.Length];
        for (var i = 0; i < runs.Length; i++)
        {
            var run = runs[i];
            built[i] = run.IsMatch
                ? new Run(run.Text) { Foreground = run.Foreground, FontFamily = AppFonts.Bold }
                : new Run(run.Text) { Foreground = run.Foreground };
        }

        inlines.AddRange(built);
        Ui.TextDirection.ApplyToInlines(block);
    }
}
