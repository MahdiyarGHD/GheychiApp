using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Controls;

public partial class SearchView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler<string>? SearchRequested;
    public event EventHandler<SearchResultItem>? SearchResultTapped;

    public SearchViewModel? Vm => DataContext as SearchViewModel;

    private SearchViewModel? _observedVm;
    private FooterList? _chatRows;
    private FooterList? _linkRows;
    private bool _pulsing;

    public SearchView()
    {
        InitializeComponent();
        SearchEntry.AddHandler(KeyDownEvent, OnSearchEntryKeyDown, RoutingStrategies.Tunnel);
        TextInputOptions.SetReturnKeyType(SearchEntry, TextInputReturnKeyType.Search);
        FiltersHeader.Text = Upper("Search_Filters");
        RecentHeader.Text = Upper("Search_Recent");

        if (DataContext is not SearchViewModel)
            DataContext = ResolveViewModel();
    }

    private static string Upper(string key) =>
        Localization.LocalizationManager.Instance[key].ToUpper(System.Globalization.CultureInfo.CurrentUICulture);

    private static SearchViewModel ResolveViewModel() =>
        IPlatformApplication.Current?.Services.GetService<SearchViewModel>() ?? new SearchViewModel();

    /// <summary>Called before every open; the heavy part (SIM list) is started here, not in the constructor.</summary>
    public void Initialize(SearchViewModel? vm = null)
    {
        var target = vm ?? Vm ?? ResolveViewModel();
        if (!ReferenceEquals(target, DataContext))
            DataContext = target;
        _ = target.InitializeAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_observedVm != null)
            _observedVm.PropertyChanged -= OnVmPropertyChanged;
        _observedVm = DataContext as SearchViewModel;

        if (_observedVm is null)
        {
            // Never leave the screen bound to a parent's view model.
            DataContext = ResolveViewModel();
            return;
        }

        _observedVm.PropertyChanged += OnVmPropertyChanged;

        _chatRows?.Detach();
        _linkRows?.Detach();
        _chatRows = new FooterList(_observedVm.SearchResultItems);
        _linkRows = new FooterList(_observedVm.SearchLinkItems);
        ChatResultsList.ItemsSource = _chatRows;
        LinkResultsList.ItemsSource = _linkRows;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_observedVm?.ShowSkeleton == true)
            StartSkeletonPulse();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopSkeletonPulse();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SearchViewModel.ShowSkeleton) or nameof(SearchViewModel.IsLinkResultsMode) or nameof(SearchViewModel.IsChatResultsMode)))
            return;

        if (_observedVm?.ShowSkeleton == true)
        {
            EnsureSkeletonRows(_observedVm);
            StartSkeletonPulse();
        }
        else
        {
            StopSkeletonPulse();
        }
    }

    // The placeholder rows are built when first needed, not with the screen.
    private void EnsureSkeletonRows(SearchViewModel vm)
    {
        var rows = vm.IsLinkMode ? LinkSkeletonRows : ChatSkeletonRows;
        rows.ItemsSource ??= vm.SkeletonRows;
    }

    private void StartSkeletonPulse()
    {
        if (_pulsing || ElementComposition.GetElementVisual(SkeletonRowsHost) is not { } visual)
            return;

        _pulsing = true;
        var easing = new SineEaseInOut();
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Target = "Opacity";
        animation.InsertKeyFrame(0f, 1f);
        animation.InsertKeyFrame(0.5f, 0.45f, easing);
        animation.InsertKeyFrame(1f, 1f, easing);
        animation.Duration = TimeSpan.FromMilliseconds(1300);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", animation);
    }

    private void StopSkeletonPulse()
    {
        if (!_pulsing)
            return;

        _pulsing = false;
        if (ElementComposition.GetElementVisual(SkeletonRowsHost) is { } visual)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = 1f;
        }
    }

    // These run as async-void callbacks; an exception escaping one terminates the process.
    private static void PostSafe(Func<Task> work) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SearchView action failed: {ex}");
            }
        });

    public void FocusSearchInput()
    {
        // Called once the overlay has slid in: build the placeholder rows now, off the slide.
        if (Vm is { } vm)
            EnsureSkeletonRows(vm);

        PostSafe(async () =>
        {
            await Task.Delay(100);
            SearchEntry.Focus();
        });
    }

    public void UnfocusSearchInput()
    {
        if (SearchEntry.IsFocused)
            Root.Focus();
    }

    public void Reset()
    {
        UnfocusSearchInput();
        ResetSearchQueryAndFilter();
    }

    public void ResetSearchQueryAndFilter()
    {
        if (Vm != null)
        {
            Vm.ClearActiveFilter();
            Vm.SearchText = string.Empty;
        }
        SearchEntry.Text = string.Empty;
    }

    /// <summary>True when the press was used to leave the results; false means the page should close the overlay.</summary>
    public bool HandleBack()
    {
        if (Vm?.IsInSearchResultsMode != true)
            return false;

        ResetSearchQueryAndFilter();
        return true;
    }

    private void SetQuery(string query)
    {
        if (Vm != null)
            Vm.SearchText = query;
        SearchEntry.Text = query;
        SearchEntry.CaretIndex = query.Length;
    }

    private void OnBackTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (Vm?.IsInSearchResultsMode == true)
        {
            ResetSearchQueryAndFilter();
        }
        else
        {
            UnfocusSearchInput();
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnClearSearchTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        SetQuery(string.Empty);
        SearchEntry.Focus();
    }

    private void OnClearActiveFilterTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        Vm?.ClearActiveFilter();
        PostSafe(async () =>
        {
            await Task.Delay(50);
            SearchEntry.Focus();
        });
    }

    private void OnVoiceSearchTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        StartVoiceSearch();
    }

    private void StartVoiceSearch()
    {
        // The recogniser is a system screen; its text comes back through MainActivity.OnActivityResult.
        void OnResult(string? text)
        {
            MainActivity.VoiceSearchCompleted -= OnResult;
            if (string.IsNullOrWhiteSpace(text))
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                SetQuery(text.Trim());
                SearchEntry.Focus();
            });
        }

        try
        {
            var activity = Platform.CurrentActivity;
            if (activity is null)
                return;

            var intent = new Android.Content.Intent(Android.Speech.RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguageModel, Android.Speech.RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguage, System.Globalization.CultureInfo.CurrentUICulture.Name);
            intent.PutExtra(Android.Speech.RecognizerIntent.ExtraPrompt, Localization.LocalizationManager.Instance["Search_VoiceSearch"] ?? "Speak to search");

            MainActivity.VoiceSearchCompleted += OnResult;
            activity.StartActivityForResult(intent, MainActivity.VoiceSearchRequestCode);
        }
        catch
        {
            // No speech recogniser on this device/emulator.
            MainActivity.VoiceSearchCompleted -= OnResult;
        }
    }

    private void OnSearchEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        var query = SearchEntry.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            Vm?.AddRecentSearch(query);
            SearchRequested?.Invoke(this, query);
        }

        // Search on the keyboard hides it, as the MAUI Entry did.
        UnfocusSearchInput();
    }

    private static T? ItemOf<T>(object? sender) where T : class => (sender as Control)?.DataContext as T;

    private void OnFilterPillTapped(object? sender, TappedEventArgs e)
    {
        if (ItemOf<FilterPillItem>(sender) is not { } pill)
            return;

        PostSafe(async () =>
        {
            await Task.Yield();
            Vm?.ApplyFilter(pill);
            await Task.Delay(80);
            SearchEntry.Focus();
        });
    }

    private void OnRecentItemTapped(object? sender, TappedEventArgs e)
    {
        var item = ItemOf<RecentSearchItem>(sender);
        if (item == null || string.IsNullOrWhiteSpace(item.Query))
            return;

        var query = item.Query.Trim();
        PostSafe(async () =>
        {
            await Task.Yield();
            SetQuery(query);
            await Task.Delay(80);
            SearchEntry.Focus();
        });
    }

    private void OnRemoveRecentItemTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (ItemOf<RecentSearchItem>(sender) is not { } item)
            return;

        PostSafe(async () =>
        {
            await Task.Yield();
            Vm?.RemoveRecentSearch(item);
        });
    }

    private void OnClearAllRecentTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        Vm?.ClearRecentSearches();
    }

    private void OnCategoryTabTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (ItemOf<CategoryTabItem>(sender) is { } tab)
            Vm?.SelectCategoryTab(tab);
    }

    private void OnSearchResultRowTapped(object? sender, TappedEventArgs e)
    {
        if (ItemOf<SearchResultItem>(sender) is not { } item)
            return;

        if (!string.IsNullOrWhiteSpace(Vm?.SearchText))
            Vm.AddRecentSearch(Vm.SearchText);
        SearchResultTapped?.Invoke(this, item);
    }

    private void OnLinkRowTapped(object? sender, TappedEventArgs e)
    {
        if (ItemOf<LinkResultItem>(sender) is not { } link)
            return;

        if (!string.IsNullOrWhiteSpace(Vm?.SearchText))
            Vm.AddRecentSearch(Vm.SearchText);

        // Same hand-off as a chat result: the page only needs the thread to open.
        SearchResultTapped?.Invoke(this, new SearchResultItem
        {
            ThreadId = link.ThreadId,
            MessageId = link.MessageId,
            Address = link.Address,
            DisplayName = link.ChatName,
            Initials = link.Initials,
            SubId = link.SubId,
            Time = link.Time
        });
    }

    private void OnLinkIconTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        var link = ItemOf<LinkResultItem>(sender);
        if (link == null || string.IsNullOrWhiteSpace(link.OpenUrl))
            return;

        PostSafe(async () =>
        {
            try
            {
                await Launcher.Default.OpenAsync(link.OpenUrl);
            }
            catch (Exception ex)
            {
                // No app can handle this link (e.g. no maps app for a geo: URI).
                System.Diagnostics.Debug.WriteLine($"Open link failed: {ex.Message}");
            }
        });
    }

    private void OnDeepSearchTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        Vm?.ToggleDeepSearch();
    }
}

/// <summary>The last row of a result list: the "search archived and spam too" button.</summary>
public sealed class DeepSearchFooter
{
    public static readonly DeepSearchFooter Instance = new();

    private DeepSearchFooter()
    {
    }
}

/// <summary>
/// A view model's result collection plus one trailing <see cref="DeepSearchFooter"/> row, so the button scrolls with the
/// rows inside the virtualised list. The footer is always last, so the source's change notifications (same indexes) are
/// forwarded untouched and the list never sees more than the source changed.
/// </summary>
internal sealed class FooterList : IList, INotifyCollectionChanged
{
    private readonly IList _source;

    public FooterList(IList source)
    {
        _source = source;
        if (source is INotifyCollectionChanged notifying)
            notifying.CollectionChanged += OnSourceChanged;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public void Detach()
    {
        if (_source is INotifyCollectionChanged notifying)
            notifying.CollectionChanged -= OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => CollectionChanged?.Invoke(this, e);

    public int Count => _source.Count + 1;

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public object? this[int index]
    {
        get => index < _source.Count ? _source[index] : DeepSearchFooter.Instance;
        set => throw new NotSupportedException();
    }

    public bool Contains(object? value) => ReferenceEquals(value, DeepSearchFooter.Instance) || _source.Contains(value);

    public int IndexOf(object? value) =>
        ReferenceEquals(value, DeepSearchFooter.Instance) ? _source.Count : _source.IndexOf(value);

    public void CopyTo(Array array, int index)
    {
        _source.CopyTo(array, index);
        array.SetValue(DeepSearchFooter.Instance, index + _source.Count);
    }

    public IEnumerator GetEnumerator()
    {
        foreach (var item in _source)
            yield return item;
        yield return DeepSearchFooter.Instance;
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();
}
