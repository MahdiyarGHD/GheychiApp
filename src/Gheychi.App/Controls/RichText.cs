using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

/// <summary>
/// <c>ctrl:RichText.Message="{Binding}"</c>: fills a TextBlock with a message's text, its links and phone numbers
/// highlighted. Only messages that have any use it; the rest stay a plain text. The inlines are rebuilt only when a
/// recycled row gets another message.
/// </summary>
public static class RichText
{
    public static readonly AttachedProperty<ChatMessage?> MessageProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, ChatMessage?>("Message", typeof(RichText));

    public static readonly AttachedProperty<IBrush?> LinkBrushProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IBrush?>("LinkBrush", typeof(RichText));

    static RichText()
    {
        MessageProperty.Changed.AddClassHandler<TextBlock>(static (block, e) => Apply(block, e.NewValue as ChatMessage));
    }

    public static ChatMessage? GetMessage(TextBlock block) => block.GetValue(MessageProperty);

    public static void SetMessage(TextBlock block, ChatMessage? value) => block.SetValue(MessageProperty, value);

    public static IBrush? GetLinkBrush(TextBlock block) => block.GetValue(LinkBrushProperty);

    public static void SetLinkBrush(TextBlock block, IBrush? value) => block.SetValue(LinkBrushProperty, value);

    /// <summary>The link at a point of the block, or null when the point is on plain text.</summary>
    public static LinkSpan? LinkAt(TextBlock block, Point point, ChatMessage message)
    {
        if (message.Links is not { } links)
            return null;

        var hit = block.TextLayout.HitTestPoint(point);
        if (!hit.IsInside)
            return null;

        foreach (var span in links)
        {
            if (hit.TextPosition >= span.Start && hit.TextPosition < span.End)
                return span;
        }

        return null;
    }

    private static void Apply(TextBlock block, ChatMessage? message)
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

        if (message is null)
            return;

        var text = message.Body;
        var built = new List<Inline>();
        var position = 0;
        var linkBrush = GetLinkBrush(block);
        foreach (var span in message.Links ?? [])
        {
            if (span.Start > position)
                built.Add(new Run(text[position..span.Start]));

            built.Add(new Run(text.Substring(span.Start, span.Length))
            {
                Foreground = linkBrush,
                TextDecorations = TextDecorations.Underline
            });
            position = span.End;
        }

        if (position < text.Length)
            built.Add(new Run(text[position..]));

        inlines.AddRange(built);
        Ui.TextDirection.ApplyToInlines(block);
    }
}
