using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Reactive;
using Gheychi.App.Localization;

namespace Gheychi.App.Ui;

/// <summary>
/// Gives each text the direction of its own first letter instead of the app's. The whole app follows the chosen
/// language, but a Persian message in an English app (or the other way round) has to read and align from its own side:
/// otherwise an emoji or a number at its start jumps to the wrong end and the punctuation is placed backwards.
/// A text with no letter keeps the app's direction, except a number (a phone number, a code such as "*123#") shown
/// by a label in a right-to-left app: that one is ordered left to right, or its groups and its signs come out backwards.
/// An emoji built from several joined by a zero-width joiner (the phoenix, a family) falls apart into its parts in a
/// right-to-left line, so a text that is only such emoji is ordered left to right as well.
/// </summary>
public static class TextDirection
{
    private static readonly FlowDirection AppDirection = CultureService.GetFlowDirection();

    /// <summary>
    /// For the previews in lists: the text is ordered by its own direction, but the line still starts on the side the
    /// app does, so a Persian preview sits where the English ones do.
    /// </summary>
    public static readonly AttachedProperty<bool> KeepSideProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, TextBlock, bool>("KeepSide");

    private static readonly AttachedProperty<bool> NumberAlignedProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, TextBlock, bool>("NumberAligned");

    private static readonly AttachedProperty<bool> BoxAlignedProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, bool>("BoxAligned");

    public static bool GetKeepSide(TextBlock control) => control.GetValue(KeepSideProperty);

    public static void SetKeepSide(TextBlock control, bool value) => control.SetValue(KeepSideProperty, value);

    /// <summary>Hooks every text block, run and text box; call once before the first one is created.</summary>
    public static void Register()
    {
        TextBlock.TextProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<string?>>(e =>
        {
            if (e.Sender is TextBlock block)
                Apply(block, e.NewValue.GetValueOrDefault());
        }));

        TextBox.TextProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<string?>>(e =>
        {
            if (e.Sender is TextBox box)
                Apply(box, e.NewValue.GetValueOrDefault());
        }));

        // A text built from runs: its text is the runs' together, and a run is changed after it is in the block.
        Run.TextProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<string?>>(e =>
        {
            if (e.Sender is Run { Parent: TextBlock block })
                ApplyToInlines(block);
        }));
    }

    public static void ApplyToInlines(TextBlock block)
    {
        if (block.Inlines is not { Count: > 0 } inlines)
        {
            Set(block, null);
            return;
        }

        var (direction, number) = DetectForLabel(string.Concat(inlines.OfType<Run>().Select(run => run.Text)));
        Set(block, direction, number);
    }

    public static void Apply(Control control, string? text)
    {
        if (control is TextBlock)
        {
            var (direction, number) = DetectForLabel(text);
            Set(control, direction, number);
        }
        else
        {
            var direction = Detect(text);
            var joined = direction is null && JoinsEmoji(text);
            Set(control, joined ? FlowDirection.LeftToRight : direction, joined);
        }
    }

    // A box someone types into keeps its direction while the first characters are digits.
    private static (FlowDirection? Direction, bool Number) DetectForLabel(string? text)
    {
        if (Detect(text) is { } direction)
            return (direction, false);

        return AppDirection == FlowDirection.RightToLeft && text is not null && (text.Any(char.IsDigit) || JoinsEmoji(text))
            ? (FlowDirection.LeftToRight, true)
            : (null, false);
    }

    private static bool JoinsEmoji(string? text) => text is not null && text.Contains('‍');

    private static void Set(Control control, FlowDirection? direction, bool number = false)
    {
        var differs = direction is { } wanted && wanted != AppDirection;
        if (differs)
            control.FlowDirection = direction!.Value;
        else if (control.IsSet(Visual.FlowDirectionProperty))
            control.ClearValue(Visual.FlowDirectionProperty);

        if (control is TextBox box)
        {
            if (differs && number)
            {
                if (!box.GetValue(BoxAlignedProperty))
                {
                    box.TextAlignment = TextAlignment.Right;
                    box.SetValue(BoxAlignedProperty, true);
                }
            }
            else if (box.GetValue(BoxAlignedProperty))
            {
                box.ClearValue(TextBox.TextAlignmentProperty);
                box.SetValue(BoxAlignedProperty, false);
            }

            return;
        }

        if (control is not TextBlock block)
            return;

        if (GetKeepSide(block))
        {
            if (differs)
                block.TextAlignment = AppDirection == FlowDirection.LeftToRight ? TextAlignment.Left : TextAlignment.Right;
            else if (block.IsSet(TextBlock.TextAlignmentProperty))
                block.ClearValue(TextBlock.TextAlignmentProperty);
        }
        else if (differs && number)
        {
            // Left to right would also move a line that starts at the edge to the other one.
            if (!block.GetValue(NumberAlignedProperty) && block.TextAlignment == TextAlignment.Start)
            {
                block.TextAlignment = TextAlignment.Right;
                block.SetValue(NumberAlignedProperty, true);
            }
        }
        else if (block.GetValue(NumberAlignedProperty))
        {
            block.ClearValue(TextBlock.TextAlignmentProperty);
            block.SetValue(NumberAlignedProperty, false);
        }
    }

    /// <summary>The direction of the first strong letter, or null when there is none.</summary>
    internal static FlowDirection? Detect(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        foreach (var c in text)
        {
            if (c == '‎')
                return FlowDirection.LeftToRight;
            if (c is '‏' or '؜')
                return FlowDirection.RightToLeft;
            if (IsRightToLeftLetter(c))
                return FlowDirection.RightToLeft;
            if (char.IsLetter(c))
                return FlowDirection.LeftToRight;
        }

        return null;
    }

    // Letters only: Arabic-Indic and Persian digits are numbers, which take the direction of the text around them.
    private static bool IsRightToLeftLetter(char c) =>
        c is >= '֐' and <= 'ࣿ' && char.IsLetter(c) ||
        c is >= 'יִ' and <= '﷿' && char.IsLetter(c) ||
        c is >= 'ﹰ' and <= '﻿' && char.IsLetter(c);
}
