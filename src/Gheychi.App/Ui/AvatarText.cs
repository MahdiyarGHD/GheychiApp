using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Gheychi.App.Theming;

namespace Gheychi.App.Ui;

/// <summary>
/// Renders short avatar text (initials or fallback symbol) with its inked glyph bounds centered inside the control
/// rather than centering the font's ascent/descent box. Ensures Latin and Persian glyphs (whose line metrics include
/// generous descender space) sit centered inside circular avatars on both LTR and RTL layouts.
/// </summary>
public sealed class AvatarText : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<AvatarText, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<AvatarText, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<AvatarText, double>(nameof(FontSize), 18);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<AvatarText, FontFamily>(nameof(FontFamily), AppFonts.Bold);

    private TextLayout? _layout;
    private Rect _inkBounds;
    private string? _cachedText;
    private FontFamily? _cachedFamily;
    private double _cachedSize;
    private IBrush? _cachedBrush;

    static AvatarText()
    {
        AffectsRender<AvatarText>(TextProperty, ForegroundProperty, FontSizeProperty, FontFamilyProperty);
        AffectsMeasure<AvatarText>(TextProperty, FontSizeProperty, FontFamilyProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<AvatarText>(false);
    }

    // MainView mirrors the whole visual tree in RTL; bypass mirroring so glyphs draw unflipped around Bounds.Width / 2.
    protected override bool BypassFlowDirectionPolicies => true;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
            return new Size(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

        var layout = GetOrCreateLayout();
        return new Size(
            double.IsInfinity(availableSize.Width) ? layout.WidthIncludingTrailingWhitespace : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? layout.Height : availableSize.Height);
    }

    public override void Render(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Text) || Foreground is null)
            return;

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var layout = GetOrCreateLayout();
        var center = _inkBounds.Width > 0 && _inkBounds.Height > 0
            ? _inkBounds.Center
            : new Point(layout.WidthIncludingTrailingWhitespace / 2, layout.Height / 2);

        var origin = new Point(bounds.Width / 2 - center.X, bounds.Height / 2 - center.Y);
        layout.Draw(context, origin);
    }

    private TextLayout GetOrCreateLayout()
    {
        var text = Text ?? string.Empty;
        var family = FontFamily;
        var size = FontSize;
        var brush = Foreground;

        if (_layout is not null &&
            _cachedText == text &&
            Equals(_cachedFamily, family) &&
            Math.Abs(_cachedSize - size) < 0.01 &&
            ReferenceEquals(_cachedBrush, brush))
        {
            return _layout;
        }

        _layout?.Dispose();
        var direction = TextDirection.Detect(text) ?? FlowDirection.LeftToRight;
        _layout = new TextLayout(
            text,
            new Typeface(family),
            size,
            brush,
            TextAlignment.Left,
            TextWrapping.NoWrap,
            flowDirection: direction);
        _inkBounds = ComputeInkBounds(_layout);
        _cachedText = text;
        _cachedFamily = family;
        _cachedSize = size;
        _cachedBrush = brush;
        return _layout;
    }

    private static Rect ComputeInkBounds(TextLayout layout)
    {
        var hasInk = false;
        Rect union = default;

        foreach (var line in layout.TextLines)
        {
            var x = line.Start;
            foreach (var run in line.TextRuns)
            {
                if (run is ShapedTextRun shaped)
                {
                    var ink = shaped.GlyphRun.InkBounds;
                    if (ink.Width > 0 && ink.Height > 0)
                    {
                        var rect = ink.Translate(new Vector(x, line.Baseline - shaped.Baseline));
                        union = hasInk ? union.Union(rect) : rect;
                        hasInk = true;
                    }
                    x += shaped.Size.Width;
                }
                else if (run is DrawableTextRun drawable)
                {
                    var rect = new Rect(x, line.Baseline - drawable.Baseline, drawable.Size.Width, drawable.Size.Height);
                    union = hasInk ? union.Union(rect) : rect;
                    hasInk = true;
                    x += drawable.Size.Width;
                }
            }
        }

        return union;
    }
}
