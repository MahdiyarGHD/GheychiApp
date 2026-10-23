using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gheychi.App.Localization;
using Gheychi.App.Services;
using Gheychi.App.Theming;
using Gheychi.App.Ui.Emoji;
using Gheychi.Core.Emoji;

namespace Gheychi.App.Controls;

/// <summary>
/// The emoji picker that takes the place of the keyboard. The host sizes it (the height of the keyboard it
/// replaced) and puts the text of <see cref="EmojiPicked"/> at the caret (<see cref="Insert"/>); <see cref="BackspaceRequested"/>
/// is the delete key (<see cref="Delete"/>), repeated while it is held.
/// Tabs at the top jump to a category and follow the list as it scrolls; below them every emoji is in one list,
/// a title above each category, with the recently used first. Nothing is built, read or measured until the panel is
/// first visible, and the list only ever holds the rows on screen.
/// </summary>
public sealed class EmojiPanel : UserControl
{
    private const double CellSize = 44;
    private const int MinColumns = 5;
    private const double TabHeight = 42;
    private const double BackspaceSize = 46;

    // One representative emoji per tab, all old enough for any phone. They stand in for icons: the tab shows the
    // picture, and the selected one gets the accent tint behind it.
    private static readonly string[] TabGlyphs =
    [
        "\U0001F552", "\U0001F600", "\U0001F44B", "\U0001F43B", "\U0001F354",
        "\U0001F697", "⚽", "\U0001F4A1", "\U0001F523", "\U0001F3C1"
    ];

    private static readonly string[] TabNames =
    [
        "Emoji_Recent", "Emoji_Smileys", "Emoji_People", "Emoji_Animals", "Emoji_Food",
        "Emoji_Travel", "Emoji_Activities", "Emoji_Objects", "Emoji_Symbols", "Emoji_Flags"
    ];

    private readonly EmojiShaper _shaper = new();
    private readonly List<Section> _sections = [];
    private readonly Border[] _tabs = new Border[TabGlyphs.Length];
    private bool _built;
    private bool _recentsChanged;
    private EmojiSet? _emoji;
    private string[] _recents = [];
    private int _columns;
    private int _selected = -1;
    private int _pinned = -1;
    private EmojiItems? _list;
    private UniformGrid? _tabRow;
    private BackspaceButton? _backspace;

    public EmojiPanel()
    {
        Background = Palette.Pick("#F1F3F4", "#121212");
        ClipToBounds = true;
    }

    /// <summary>An emoji was tapped; the text to put at the caret.</summary>
    public event Action<string>? EmojiPicked;

    /// <summary>The delete key was pressed, and again at intervals while it stays down.</summary>
    public event Action? BackspaceRequested;

    /// <summary>Back to the top of the list, and the recents list brought up to date. Call it each time the panel is shown.</summary>
    public void Reset()
    {
        if (!_built)
            return;

        if (_emoji is null)
        {
            _ = LoadAsync();
            return;
        }

        if (_recentsChanged)
            Rebuild(keepPlace: false);
        else if (_list?.Scroll is { } scroll)
            scroll.Offset = default;

        _pinned = -1;
        Select(_sections.Count > 0 ? 0 : -1);
    }

    /// <summary>Puts <paramref name="emoji"/> at the caret of <paramref name="box"/>, in place of its selection.</summary>
    public static void Insert(TextBox box, string emoji)
    {
        var (text, caret) = EmojiTextEdit.Insert(box.Text, box.SelectionStart, box.SelectionEnd, emoji);
        box.Text = text;
        box.CaretIndex = caret;
    }

    /// <summary>The delete key in <paramref name="box"/>: the selection, or the emoji (or letter) before the caret.</summary>
    public static void Delete(TextBox box)
    {
        var (text, caret) = EmojiTextEdit.Backspace(box.Text, box.SelectionStart, box.SelectionEnd);
        box.Text = text;
        box.CaretIndex = caret;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BuildWhenShown();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            BuildWhenShown();
    }

    // Measured only while shown: also catches a panel made visible through a parent, which raises no change here.
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!_built)
            Dispatcher.UIThread.Post(BuildWhenShown, DispatcherPriority.Loaded);
        return base.MeasureOverride(availableSize);
    }

    private void BuildWhenShown()
    {
        if (!_built && IsEffectivelyVisible)
            Build();
    }

    private void Build()
    {
        _built = true;
        _recents = EmojiRecents.Parse(AppPreferences.RecentEmoji);

        _list = new EmojiItems(_shaper) { Margin = default };
        _list.AddHandler(TappedEvent, OnListTapped);
        _list.Scrolled += (_, _) => OnScrolled();
        _list.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width != e.PreviousSize.Width)
                // Not inside this layout pass: the extent of the new list only exists after it, and an offset set
                // before then is clamped to the top.
                Dispatcher.UIThread.Post(() => Rebuild(keepPlace: true), DispatcherPriority.Loaded);
        };

        _tabRow = new UniformGrid { Rows = 1, Margin = new Thickness(6, 4) };
        for (var i = 0; i < _tabs.Length; i++)
            _tabs[i] = BuildTab(i);

        _backspace = new BackspaceButton { [Grid.RowProperty] = 2 };
        _backspace.Pressed += () => BackspaceRequested?.Invoke();

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        root.Children.Add(_tabRow);
        root.Children.Add(new Border
        {
            [Grid.RowProperty] = 1,
            Height = 1,
            Background = Palette.Pick("#E3E5E8", "#26292E")
        });
        root.Children.Add(new Border { [Grid.RowProperty] = 2, Child = _list });
        root.Children.Add(_backspace);
        Content = root;

        _ = LoadAsync();
    }

    private Border BuildTab(int index)
    {
        var glyph = new TextBlock
        {
            Text = TabGlyphs[index],
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Palette.Accent(AccentRole.Text),
            IsHitTestVisible = false
        };

        var tab = new Border
        {
            Height = TabHeight - 8,
            Margin = new Thickness(1, 0),
            CornerRadius = new CornerRadius(14),
            Background = Brushes.Transparent,
            Child = glyph,
            Opacity = 0.55
        };
        Avalonia.Automation.AutomationProperties.SetName(tab, LocalizationManager.Instance[TabNames[index]]);
        tab.Tapped += (_, _) => GoTo(index);
        return tab;
    }

    private async Task LoadAsync()
    {
        try
        {
            _emoji = await EmojiData.LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Loading the emoji failed: {ex}");
            return;
        }

        Rebuild(keepPlace: false);
        Select(_sections.Count > 0 ? 0 : -1);
    }

    /// <summary>
    /// Lays the categories out as rows for the width in hand. Every row is as tall as every other, which is what lets
    /// a category's place in the list be worked out (a title is a row of its own) instead of measured.
    /// </summary>
    private void Rebuild(bool keepPlace)
    {
        if (_emoji is not { } emoji || _list is not { } list || list.Bounds.Width <= 0)
            return;

        var columns = Math.Max(MinColumns, (int)(list.Bounds.Width / CellSize));
        var category = keepPlace && _selected >= 0 && _selected < _sections.Count ? _sections[_selected].Category : (EmojiCategory?)null;

        _sections.Clear();
        var rows = new List<EmojiRowItem>();
        var recents = _recents.Where(emoji.Contains).ToArray();

        void Add(EmojiCategory kind, string[] items)
        {
            if (items.Length == 0)
                return;

            _sections.Add(new Section(kind, rows.Count * EmojiRowView.RowHeight));
            rows.Add(new EmojiRowItem(LocalizationManager.Instance[TabNames[(int)kind]], null));
            for (var i = 0; i < items.Length; i += columns)
                rows.Add(new EmojiRowItem(null, items.AsSpan(i, Math.Min(columns, items.Length - i)).ToArray()));
        }

        Add(EmojiCategory.Recent, recents);
        for (var i = 0; i < emoji.Categories.Length && i + 1 < TabGlyphs.Length; i++)
            Add((EmojiCategory)(i + 1), emoji.Categories[i]);

        _columns = columns;
        _recentsChanged = false;
        list.Columns = columns;
        list.Rtl = FlowDirection == FlowDirection.RightToLeft;
        list.ItemsSource = rows;

        _tabRow!.Children.Clear();
        foreach (var section in _sections)
            _tabRow.Children.Add(_tabs[(int)section.Category]);
        _tabRow.Columns = Math.Max(_sections.Count, 1);

        var top = category is { } wanted && _sections.FindIndex(s => s.Category == wanted) is var at and >= 0
            ? _sections[at].Top
            : 0;
        if (list.Scroll is { } scroll)
        {
            list.UpdateLayout();
            scroll.Offset = new Vector(0, top);
        }

        _selected = -1;
        Select(Math.Max(SectionAt(top), 0));
    }

    private void GoTo(int tab)
    {
        var index = _sections.FindIndex(s => (int)s.Category == tab);
        if (index < 0 || _list?.Scroll is not { } scroll)
            return;

        _pinned = index;
        scroll.Offset = new Vector(0, _sections[index].Top);
        Select(index);
    }

    private void OnScrolled()
    {
        if (_list?.Scroll is not { } scroll || _sections.Count == 0)
            return;

        var section = SectionAt(scroll.Offset.Y);

        // A category near the end cannot reach the top of the list, so the list rests on an earlier one: the tab the
        // finger chose stays until the list is moved from there.
        if (_pinned >= 0)
        {
            var atEnd = scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 1;
            if (section == _pinned)
                _pinned = -1;
            else if (atEnd && section < _pinned)
                section = _pinned;
            else
                _pinned = -1;
        }

        Select(section);
    }

    private int SectionAt(double offset)
    {
        var found = -1;
        for (var i = 0; i < _sections.Count && _sections[i].Top <= offset + 1; i++)
            found = i;
        return found;
    }

    private void Select(int section)
    {
        if (section == _selected)
            return;

        _selected = section;
        for (var i = 0; i < _tabs.Length; i++)
        {
            var on = section >= 0 && section < _sections.Count && (int)_sections[section].Category == i;
            _tabs[i].Background = on ? Palette.Accent(AccentRole.Soft) : Brushes.Transparent;
            _tabs[i].Opacity = on ? 1 : 0.55;
        }
    }

    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source || source.GetSelfAndVisualAncestors().OfType<EmojiRowView>().FirstOrDefault() is not { } row)
            return;

        if (row.CellAt(e.GetPosition(row).X) is not { } emoji)
            return;

        _recents = EmojiRecents.Push(_recents, emoji);
        _recentsChanged = true;
        AppPreferences.RecentEmoji = EmojiRecents.Serialize(_recents);
        EmojiPicked?.Invoke(emoji);
    }

    private readonly record struct Section(EmojiCategory Category, double Top);

    /// <summary>
    /// The list: a virtualised column of <see cref="EmojiRowView"/>. As in the chat list, a row is its own container
    /// and is handed the next item when recycled, so scrolling only rebinds rows.
    /// </summary>
    private sealed class EmojiItems : ItemsControl
    {
        private readonly EmojiShaper _shaper;

        public EmojiItems(EmojiShaper shaper)
        {
            _shaper = shaper;
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
            Template = new FuncControlTemplate<EmojiItems>((owner, scope) =>
            {
                var scroll = new ScrollViewer
                {
                    Name = "PART_Scroll",
                    // Room under the last row for the delete key.
                    Padding = new Thickness(0, 0, 0, BackspaceSize + 20),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    Content = new ItemsPresenter { ItemsPanel = owner.ItemsPanel }
                };
                scope.Register("PART_Scroll", scroll);
                return scroll;
            });
        }

        public int Columns { get; set; } = 8;

        public bool Rtl { get; set; }

        public ScrollViewer? Scroll { get; private set; }

        public event EventHandler<ScrollChangedEventArgs>? Scrolled;

        protected override Type StyleKeyOverride => typeof(ItemsControl);

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
            recycleKey = null;
            return true;
        }

        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
            new EmojiRowView(_shaper);

        protected override void PrepareContainerForItemOverride(Control container, object? item, int index) =>
            ((EmojiRowView)container).Bind((EmojiRowItem)item!, Columns, Rtl);

        protected override void ClearContainerForItemOverride(Control container)
        {
        }
    }

    /// <summary>
    /// The delete key, a round button at the end of the panel's bottom edge. Pressing it deletes at once; holding
    /// it deletes again after a pause and then quickly, until it is released.
    /// </summary>
    private sealed class BackspaceButton : Border
    {
        private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan Repeat = TimeSpan.FromMilliseconds(55);

        private readonly DispatcherTimer _timer = new();

        public BackspaceButton()
        {
            Width = Height = BackspaceSize;
            CornerRadius = new CornerRadius(BackspaceSize / 2);
            Margin = new Thickness(0, 0, 14, 14);
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            Background = Palette.Pick("#FFFFFF", "#2A2D33");
            BorderBrush = Palette.Pick("#E3E5E8", "#383C43");
            BorderThickness = new Thickness(1);
            Focusable = false;

            // The Material "backspace" glyph.
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M22 3H7c-.69 0-1.23.35-1.59.88L0 12l5.41 8.11c.36.53.9.89 1.59.89h15c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-3 12.59L17.59 17 14 13.41 10.41 17 9 15.59 12.59 12 9 8.41 10.41 7 14 10.59 17.59 7 19 8.41 15.41 12 19 15.59z"),
                Fill = Palette.Pick("#3C4043", "#E8EAED"),
                Stretch = Stretch.Uniform,
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            Avalonia.Automation.AutomationProperties.SetName(this, LocalizationManager.Instance["Emoji_Backspace"]);
            _timer.Tick += (_, _) =>
            {
                _timer.Interval = Repeat;
                Pressed?.Invoke();
            };
        }

        public event Action? Pressed;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            e.Pointer.Capture(this);
            e.Handled = true;

            Pressed?.Invoke();
            _timer.Interval = FirstDelay;
            _timer.Start();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _timer.Stop();
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _timer.Stop();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _timer.Stop();
        }
    }
}
