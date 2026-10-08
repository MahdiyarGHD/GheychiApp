using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Controls;

public enum RowAlignment
{
    Start,
    Center,
    End
}

/// <summary>A row of the list that was on screen: the item and where its top edge was, relative to the viewport.</summary>
internal readonly record struct ScrollAnchor(object Item, double Top);

/// <summary>
/// The chat's message list. Rows are virtualised (VirtualizingStackPanel) and each row is its own container: a row
/// built from a template is bound to the next item of the same kind instead of being rebuilt (a ContentPresenter
/// container would throw its content away whenever it is recycled), so scrolling only changes data contexts.
/// Anchoring helpers keep the reader's place when rows are prepended and jump to a given row.
/// </summary>
internal sealed class ChatList : ItemsControl
{
    public ChatTemplateSelector? Rows { get; set; }

    public ScrollViewer? Scroll { get; private set; }

    public VirtualizingStackPanel? Panel => ItemsPanelRoot as VirtualizingStackPanel;

    /// <summary>Raised when the offset, the extent or the viewport changed.</summary>
    public event EventHandler<ScrollChangedEventArgs>? Scrolled;

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    public int FirstVisibleIndex => Panel?.FirstRealizedIndex ?? -1;

    public int LastVisibleIndex => Panel?.LastRealizedIndex ?? -1;

    /// <summary>True when the newest row is on screen and the list rests on its end.</summary>
    public bool IsAtBottom =>
        Scroll is not { } scroll ||
        ItemCount == 0 ||
        (LastVisibleIndex >= ItemCount - 1 && scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 1);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (Scroll is { } old)
            old.ScrollChanged -= OnScrollChanged;

        Scroll = e.NameScope.Find<ScrollViewer>("PART_Scroll");
        if (Scroll is { } scroll)
            scroll.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => Scrolled?.Invoke(this, e);

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = ChatTemplateSelector.KeyOf(item);
        return true;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        (Rows ?? throw new InvalidOperationException(nameof(Rows))).BuildRow(ChatTemplateSelector.KindOf(item));

    // The old item stays bound while a row is parked: clearing the context would refresh every binding of the row
    // twice (to nothing, then to the next item).
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index) =>
        container.DataContext = item;

    protected override void ClearContainerForItemOverride(Control container)
    {
    }

    /// <summary>Puts the end of the list in view. Bring the newest row into view first: the extent is only an estimate until it is realised.</summary>
    public void ScrollToEnd()
    {
        if (ItemCount == 0 || Scroll is not { } scroll)
            return;

        UpdateLayout();
        ScrollIntoView(ItemCount - 1);
        UpdateLayout();
        scroll.ScrollToEnd();
        UpdateLayout();
    }

    /// <summary>
    /// Scrolls so the row at <paramref name="index"/> sits at the top, the middle or the bottom of the viewport.
    /// Rows have a margin that is part of their slot, so it is counted in the position.
    /// </summary>
    public void ScrollToIndex(int index, RowAlignment alignment)
    {
        if (index < 0 || index >= ItemCount || Scroll is not { } scroll)
            return;

        UpdateLayout();
        ScrollIntoView(index);
        UpdateLayout();

        if (ContainerFromIndex(index) is not { } row || TopOf(row, scroll) is not { } top)
            return;

        var margin = row.Margin;
        var slotTop = top - margin.Top;
        var slotHeight = row.Bounds.Height + margin.Top + margin.Bottom;
        var viewport = scroll.Viewport.Height;
        var wanted = alignment switch
        {
            RowAlignment.Start => 0,
            RowAlignment.Center => (viewport - slotHeight) / 2,
            _ => viewport - slotHeight
        };

        SetOffset(scroll, scroll.Offset.Y + (slotTop - wanted));
    }

    public ScrollAnchor[] CaptureAnchors()
    {
        if (Scroll is not { } scroll || Panel is not { } panel || ItemsSource is not IList items)
            return [];

        var anchors = new List<ScrollAnchor>(3);
        var first = panel.FirstRealizedIndex;
        var last = Math.Min(panel.LastRealizedIndex, first + 2);
        for (var i = Math.Max(first, 0); i <= last && i < items.Count; i++)
        {
            if (ContainerFromIndex(i) is { } row && TopOf(row, scroll) is { } top)
                anchors.Add(new ScrollAnchor(items[i]!, top - row.Margin.Top));
        }

        return [.. anchors];
    }

    /// <summary>
    /// Puts the first captured row that is still in the list back where it was. A prepend moves the rows the reader
    /// is looking at far down the list, and the panel keeps the old offset: this brings them back in one pass, before
    /// a frame is drawn.
    /// </summary>
    public void RestoreAnchor(ScrollAnchor[] anchors)
    {
        if (Scroll is not { } scroll || ItemsSource is not IList items)
            return;

        foreach (var anchor in anchors)
        {
            var index = items.IndexOf(anchor.Item);
            if (index < 0)
                continue;

            UpdateLayout();
            ScrollIntoView(index);
            UpdateLayout();
            if (ContainerFromIndex(index) is not { } row || TopOf(row, scroll) is not { } top)
                return;

            SetOffset(scroll, scroll.Offset.Y + (top - row.Margin.Top - anchor.Top));
            UpdateLayout();
            return;
        }
    }

    private static double? TopOf(Control row, ScrollViewer scroll) => row.TranslatePoint(default, scroll)?.Y;

    private static void SetOffset(ScrollViewer scroll, double y) =>
        scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, y));
}
