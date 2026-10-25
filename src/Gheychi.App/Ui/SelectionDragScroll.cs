using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Gheychi.App.Ui;

/// <summary>
/// Keeps a text box from scrolling under the finger while a selection handle (or the caret, by touch) is dragged.
/// Avalonia scrolls the caret into view on every move, and a handle dragged to the end of the last line in view puts
/// the caret on the start of the next, hidden line: the box scrolls a line, the finger is now over the next line, and
/// the selection runs to the end of the text. While a drag is on, the box only scrolls when the finger is taken past
/// its edge, one line per step. Switched on for every text box by a style (<c>ui:SelectionDragScroll.Enabled</c>).
/// </summary>
public static class SelectionDragScroll
{
    // A handle hangs below its line, so the finger holding the end handle of the last line is already under the box.
    private const double EdgeMargin = 28;
    private static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(150);

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, bool>("Enabled");

    private static IPointer? _dragPointer;
    private static Visual? _dragRoot;
    private static Point _finger;
    private static DateTime _lastStep;

    static SelectionDragScroll()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            if (e.GetNewValue<bool>())
            {
                box.TemplateApplied += OnTemplateApplied;
                if (box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is { } presenter)
                    Watch(presenter);
            }
            else
            {
                box.TemplateApplied -= OnTemplateApplied;
            }
        });

        // Tunnelling from the window, so the drag is known before the handle or the box acts on the move.
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // A release that never arrives (the pointer taken by a popup) must not leave typing unable to scroll to the caret.
        InputElement.TextInputEvent.AddClassHandler<TopLevel>((_, _) => EndDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);

    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<TextPresenter>("PART_TextPresenter") is { } presenter)
            Watch(presenter);
    }

    private static void Watch(TextPresenter presenter)
    {
        presenter.RemoveHandler(Control.RequestBringIntoViewEvent, OnBringIntoView);
        presenter.AddHandler(Control.RequestBringIntoViewEvent, OnBringIntoView);
    }

    private static void OnPressed(TopLevel root, PointerPressedEventArgs e)
    {
        EndDrag();
        if (e.Source is not Visual source)
            return;

        var onHandle = source as TextSelectionHandle ?? source.FindAncestorOfType<TextSelectionHandle>();
        var onText = e.Pointer.Type != PointerType.Mouse && (source as TextPresenter ?? source.FindAncestorOfType<TextPresenter>()) is not null;
        if (onHandle is null && !onText)
            return;

        _dragPointer = e.Pointer;
        _dragRoot = root;
        _finger = e.GetPosition(root);
    }

    private static void OnMoved(TopLevel root, PointerEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _dragPointer))
            _finger = e.GetPosition(root);
    }

    private static void OnReleased(TopLevel root, PointerReleasedEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _dragPointer))
            EndDrag();
    }

    private static void EndDrag()
    {
        _dragPointer = null;
        _dragRoot = null;
    }

    // Handled here, on the presenter, the request never reaches the box's scroll viewer.
    private static void OnBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (_dragPointer is null || _dragRoot is null || sender is not Visual presenter ||
            presenter.FindAncestorOfType<ScrollViewer>() is not { } scroll ||
            _dragRoot.TranslatePoint(_finger, scroll) is not { } finger)
            return;

        var now = DateTime.UtcNow;
        if (!new Rect(scroll.Bounds.Size).Inflate(EdgeMargin).Contains(finger) && now - _lastStep >= StepInterval)
        {
            _lastStep = now;
            return;
        }

        e.Handled = true;
    }
}
