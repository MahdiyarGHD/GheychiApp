using Gheychi.App.ViewModels;

namespace Gheychi.App.Controls;

public partial class SearchView : ContentView
{
    public event EventHandler? BackRequested;
    public event EventHandler<string>? SearchRequested;
    public event EventHandler<SearchResultItem>? SearchResultTapped;

    public SearchViewModel? Vm => BindingContext as SearchViewModel;

    public SearchView()
    {
        InitializeComponent();
        var vm = IPlatformApplication.Current?.Services.GetService<SearchViewModel>() ?? new SearchViewModel();
        BindingContext = vm;
        _ = vm.InitializeAsync();
    }

    public void Initialize(SearchViewModel? vm = null)
    {
        var targetVm = vm ?? (BindingContext as SearchViewModel) ?? IPlatformApplication.Current?.Services.GetService<SearchViewModel>() ?? new SearchViewModel();
        BindingContext = targetVm;
        _ = targetVm.InitializeAsync();
        UpdateTrailingButtons(SearchEntry.Text);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_observedVm != null)
            _observedVm.PropertyChanged -= OnVmPropertyChanged;
        _observedVm = BindingContext as SearchViewModel;
        if (_observedVm != null)
            _observedVm.PropertyChanged += OnVmPropertyChanged;

        if (BindingContext is not SearchViewModel)
        {
            var vm = IPlatformApplication.Current?.Services.GetService<SearchViewModel>() ?? new SearchViewModel();
            BindingContext = vm;
            _ = vm.InitializeAsync();
        }
    }

    private SearchViewModel? _observedVm;
    private CancellationTokenSource? _pulseCts;

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SearchViewModel.ShowSkeleton))
            return;

        if (_observedVm?.ShowSkeleton == true)
            StartSkeletonPulse();
        else
            StopSkeletonPulse();
    }

    private void StartSkeletonPulse()
    {
        if (_pulseCts != null)
            return;

        var cts = new CancellationTokenSource();
        _pulseCts = cts;
        _ = PulseAsync(cts.Token);
    }

    private void StopSkeletonPulse()
    {
        _pulseCts?.Cancel();
        _pulseCts = null;
        SkeletonRowsHost.CancelAnimations();
        SkeletonRowsHost.Opacity = 1;
    }

    private async Task PulseAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await SkeletonRowsHost.FadeToAsync(0.45, 650, Easing.SinInOut);
                if (token.IsCancellationRequested)
                    break;
                await SkeletonRowsHost.FadeToAsync(1, 650, Easing.SinInOut);
            }
        }
        catch (Exception)
        {
            // Animation is cosmetic; it can fail if the view is torn down mid-fade.
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
        PostSafe(async () =>
        {
            await Task.Delay(100);
            SearchEntry.Focus();
        });
    }

    public void UnfocusSearchInput()
    {
        SearchEntry.Unfocus();
    }

    public void Reset()
    {
        UnfocusSearchInput();
        if (Vm != null)
        {
            Vm.ClearActiveFilter();
            Vm.SearchText = string.Empty;
        }
        SearchEntry.Text = string.Empty;
        UpdateTrailingButtons(string.Empty);
    }

    public void ResetSearchQueryAndFilter()
    {
        if (Vm != null)
        {
            Vm.ClearActiveFilter();
            Vm.SearchText = string.Empty;
        }
        SearchEntry.Text = string.Empty;
        UpdateTrailingButtons(string.Empty);
    }

    private void OnBackTapped(object? sender, EventArgs e)
    {
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

    private void OnSearchEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateTrailingButtons(e.NewTextValue);
    }

    private void UpdateTrailingButtons(string? text)
    {
        var hasText = !string.IsNullOrWhiteSpace(text);
        if (MicButton != null)
            MicButton.IsVisible = !hasText;
        if (ClearButton != null)
            ClearButton.IsVisible = hasText;
    }

    private void OnClearSearchTapped(object? sender, EventArgs e)
    {
        SearchEntry.Text = string.Empty;
        if (Vm != null)
            Vm.SearchText = string.Empty;
        UpdateTrailingButtons(string.Empty);
        SearchEntry.Focus();
    }

    private void OnClearActiveFilterTapped(object? sender, EventArgs e)
    {
        Vm?.ClearActiveFilter();
        PostSafe(async () =>
        {
            await Task.Delay(50);
            SearchEntry.Focus();
        });
    }

    private void OnVoiceSearchTapped(object? sender, EventArgs e)
    {
        StartVoiceSearch();
    }

    private void StartVoiceSearch()
    {
#if ANDROID
        try
        {
            var intent = new Android.Content.Intent(Android.Speech.RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguageModel, Android.Speech.RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(Android.Speech.RecognizerIntent.ExtraPrompt, Localization.LocalizationManager.Instance["Search_VoiceSearch"] ?? "Speak to search");
            Platform.CurrentActivity?.StartActivity(intent);
        }
        catch
        {
            // Voice search not available on device/emulator
        }
#endif
    }

    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        var query = SearchEntry.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            Vm?.AddRecentSearch(query);
            SearchRequested?.Invoke(this, query);
        }
    }

    private void OnFilterPillTapped(object? sender, TappedEventArgs e)
    {
        var pill = (e.Parameter as FilterPillItem) ?? (sender as Element)?.BindingContext as FilterPillItem;
        if (pill == null)
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
        var item = (e.Parameter as RecentSearchItem) ?? (sender as Element)?.BindingContext as RecentSearchItem;
        if (item == null || string.IsNullOrWhiteSpace(item.Query))
            return;

        var query = item.Query.Trim();
        PostSafe(async () =>
        {
            await Task.Yield();
            if (Vm != null)
                Vm.SearchText = query;
            SearchEntry.Text = query;
            await Task.Delay(80);
            SearchEntry.Focus();
        });
    }

    private void OnRemoveRecentItemTapped(object? sender, TappedEventArgs e)
    {
        var item = (e.Parameter as RecentSearchItem) ?? (sender as Element)?.BindingContext as RecentSearchItem;
        if (item != null)
        {
            PostSafe(async () =>
            {
                await Task.Yield();
                Vm?.RemoveRecentSearch(item);
            });
        }
    }

    private void OnClearAllRecentTapped(object? sender, EventArgs e)
    {
        Vm?.ClearRecentSearches();
    }

    private void OnCategoryTabTapped(object? sender, TappedEventArgs e)
    {
        var tab = (e.Parameter as CategoryTabItem) ?? (sender as Element)?.BindingContext as CategoryTabItem;
        if (tab != null)
        {
            Vm?.SelectCategoryTab(tab);
        }
    }

    private void OnSearchResultRowTapped(object? sender, TappedEventArgs e)
    {
        var item = (e.Parameter as SearchResultItem) ?? (sender as Element)?.BindingContext as SearchResultItem;
        if (item != null)
        {
            if (!string.IsNullOrWhiteSpace(Vm?.SearchText))
                Vm.AddRecentSearch(Vm.SearchText);
            SearchResultTapped?.Invoke(this, item);
        }
    }

    private void OnLinkRowTapped(object? sender, TappedEventArgs e)
    {
        var link = (e.Parameter as LinkResultItem) ?? (sender as Element)?.BindingContext as LinkResultItem;
        if (link == null)
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
        var link = (e.Parameter as LinkResultItem) ?? (sender as Element)?.BindingContext as LinkResultItem;
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

    private void OnDeepSearchTapped(object? sender, EventArgs e)
    {
        Vm?.ToggleDeepSearch();
    }
}
