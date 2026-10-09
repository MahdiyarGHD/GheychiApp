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
/// A text with no letter (a time, a count, only emoji) keeps the app's direction.
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

        foreach (var inline in inlines)
        {
            if (inline is Run { Text: { Length: > 0 } text } && Detect(text) is { } direction)
            {
                Set(block, direction);
                return;
            }
        }

        Set(block, null);
    }

    public static void Apply(Control control, string? text) => Set(control, Detect(text));

    private static void Set(Control control, FlowDirection? direction)
    {
        var differs = direction is { } wanted && wanted != AppDirection;
        if (differs)
            control.FlowDirection = direction!.Value;
        else if (control.IsSet(Visual.FlowDirectionProperty))
            control.ClearValue(Visual.FlowDirectionProperty);

        if (control is TextBlock block && GetKeepSide(block))
        {
            if (differs)
                block.TextAlignment = AppDirection == FlowDirection.LeftToRight ? TextAlignment.Left : TextAlignment.Right;
            else if (block.IsSet(TextBlock.TextAlignmentProperty))
                block.ClearValue(TextBlock.TextAlignmentProperty);
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
