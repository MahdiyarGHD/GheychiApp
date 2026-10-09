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

    // A fingertip is far wider than a line of text: a tap this close to a link still means the link.
    private const double TouchSlop = 14;

    /// <summary>A link of a message and the line of text it was found on, in the coordinates of the block.</summary>
    public readonly record struct LinkHit(LinkSpan Span, Rect Bounds);

    /// <summary>
    /// The link nearest to a point of the block, if one is within reach of a finger. Tested against the rectangles the
    /// links are drawn in, not against the character under the point: that one is only right for a pointer, and a
    /// finger lands beside or between the lines.
    /// </summary>
    public static LinkHit? LinkAt(TextBlock block, Point point, ChatMessage message)
    {
        if (message.Links is not { } links)
            return null;

        var layout = block.TextLayout;
        LinkHit? best = null;
        var bestDistance = TouchSlop * TouchSlop;
        foreach (var span in links)
        {
            foreach (var rect in layout.HitTestTextRange(span.Start, span.Length))
            {
                var dx = Math.Max(Math.Max(rect.Left - point.X, point.X - rect.Right), 0);
                var dy = Math.Max(Math.Max(rect.Top - point.Y, point.Y - rect.Bottom), 0);
                var distance = dx * dx + dy * dy;
                if (distance > bestDistance)
                    continue;

                bestDistance = distance;
                best = new LinkHit(span, rect);
            }
        }

        return best;
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
