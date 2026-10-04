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
        if (BindingContext is not SearchViewModel)
        {
            var vm = IPlatformApplication.Current?.Services.GetService<SearchViewModel>() ?? new SearchViewModel();
            BindingContext = vm;
            _ = vm.InitializeAsync();
        }
    }

    public void FocusSearchInput()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
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
        MainThread.BeginInvokeOnMainThread(async () =>
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

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Yield();
            Vm?.ApplyFilter(pill);
            if (pill.FilterKind == Gheychi.Core.Models.SearchFilterKind.None)
            {
                SearchEntry.Text = pill.QueryPrefix;
            }
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
        MainThread.BeginInvokeOnMainThread(async () =>
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
            MainThread.BeginInvokeOnMainThread(async () =>
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

    private void OnDeepSearchTapped(object? sender, EventArgs e)
    {
        Vm?.ToggleDeepSearch();
    }
}
