using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

public sealed class RecentSearchItem
{
    public string Query { get; set; } = string.Empty;

    public RecentSearchItem() { }

    public RecentSearchItem(string query)
    {
        Query = query;
    }
}

public sealed class FilterPillItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string QueryPrefix { get; set; } = string.Empty;
    public SearchFilterKind FilterKind { get; set; } = SearchFilterKind.None;
    public int? SimSlot { get; set; }
    public string IconSource { get; set; } = string.Empty;
    public Color IconTint { get; set; } = Colors.Gray;

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public enum CategoryFilterKind
{
    All,
    Unread,
    Starred,
    Known,
    Unknown,
    Sim
}

public sealed class CategoryTabItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            if (SetField(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string CountText => Count.ToString();

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BackgroundColor));
                OnPropertyChanged(nameof(TextColor));
                OnPropertyChanged(nameof(BadgeBgColor));
                OnPropertyChanged(nameof(BadgeTextColor));
                OnPropertyChanged(nameof(FontFamily));
            }
        }
    }

    public CategoryFilterKind Kind { get; set; } = CategoryFilterKind.All;
    public int? SimSlot { get; set; }
    public bool ShowBadge { get; set; }

    private static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    public Color BackgroundColor => IsSelected
        ? (IsDark ? Color.FromArgb("#34D399") : Color.FromArgb("#386948"))
        : (IsDark ? Color.FromArgb("#1E2320") : Color.FromArgb("#ECE1D3"));

    public Color TextColor => IsSelected
        ? (IsDark ? Color.FromArgb("#121413") : Colors.White)
        : (IsDark ? Color.FromArgb("#DCE5DB") : Color.FromArgb("#59615A"));

    public Color BadgeBgColor => IsSelected
        ? (IsDark ? Color.FromArgb("#33000000") : Color.FromArgb("#33FFFFFF"))
        : (IsDark ? Color.FromArgb("#33FFFFFF") : Color.FromArgb("#1F000000"));

    public Color BadgeTextColor => TextColor;

    public string FontFamily => IsSelected
        ? ThreadItem.FontFamilyBold
        : ThreadItem.FontFamilyRegular;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class SearchResultItem
{
    public long ThreadId { get; init; }
    public long MessageId { get; init; }
    public string Address { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public int SubId { get; init; }
    public int SimSlot { get; init; } = 1;
    public string SimSlotText => SimSlot.ToString();
    public bool ShowSimBadge { get; init; }
    public Color SimBadgeColor { get; init; } = Colors.Gray;
    public string Time { get; init; } = string.Empty;
    public bool IsUnread { get; init; }
    public bool IsStarred { get; init; }
    public bool IsKnown { get; init; }
    public int TotalMatches { get; init; } = 1;
    public bool HasMultipleMatches => TotalMatches > 1;
    public string MatchCountText => TotalMatches > 1
        ? string.Format(Localization.LocalizationManager.Instance["Search_MatchesPlural"] ?? "• {0} matches", TotalMatches)
        : string.Empty;

    public FormattedString FormattedSnippet { get; init; } = new();
    public bool IsArchived { get; init; }
    public bool IsSpam { get; init; }
    public bool IsArchivedOrSpam => IsArchived || IsSpam;

    public string DeepSearchTag => IsSpam
        ? (Localization.LocalizationManager.Instance["Search_Tag_Spam"] ?? "Spam")
        : (Localization.LocalizationManager.Instance["Search_Tag_Archived"] ?? "Archived");

    private static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    public Color TimeColor => IsUnread
        ? (IsDark ? Color.FromArgb("#34D399") : Color.FromArgb("#386948"))
        : (IsDark ? Color.FromArgb("#8A8F98") : Color.FromArgb("#747D75"));

    public Color AvatarBgColor => IsDark ? Color.FromArgb("#35423C") : Color.FromArgb("#E3E9E4");
    public Color AvatarTextColor => IsDark ? Color.FromArgb("#8FE0BE") : Color.FromArgb("#1B5E43");
}

public sealed class SearchViewModel : INotifyPropertyChanged
{
    private const string RecentSearchesKey = "search_recent_queries_v1";
    private readonly ISmsService? _smsService;
    private readonly IDateFormattingService _dateFormatter;

    private CancellationTokenSource? _searchCts;
    private readonly List<SearchResultItem> _allSearchResults = [];
    private IReadOnlyDictionary<int, int> _simSlotMap = new Dictionary<int, int>();
    private IReadOnlyList<SimCardInfo> _activeSims = [];
    private bool _isDualSim;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                OnPropertyChanged(nameof(HasSearchText));
                OnPropertyChanged(nameof(IsSearchTextEmpty));
                OnPropertyChanged(nameof(IsInSearchResultsMode));
                OnPropertyChanged(nameof(IsInitialSearchMode));
                TriggerDebouncedSearch();
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool IsSearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

    private FilterPillItem? _activeFilter;
    public FilterPillItem? ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (SetField(ref _activeFilter, value))
            {
                OnPropertyChanged(nameof(HasActiveFilter));
                OnPropertyChanged(nameof(HasNoActiveFilter));
                OnPropertyChanged(nameof(IsInSearchResultsMode));
                OnPropertyChanged(nameof(IsInitialSearchMode));
                TriggerDebouncedSearch();
            }
        }
    }

    public bool HasActiveFilter => ActiveFilter != null;
    public bool HasNoActiveFilter => ActiveFilter == null;

    public bool IsInSearchResultsMode => HasActiveFilter || HasSearchText;
    public bool IsInitialSearchMode => !IsInSearchResultsMode;

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetField(ref _isSearching, value))
            {
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    }

    private bool _isDeepSearchActive;
    public bool IsDeepSearchActive
    {
        get => _isDeepSearchActive;
        set
        {
            if (SetField(ref _isDeepSearchActive, value))
            {
                TriggerDebouncedSearch();
            }
        }
    }

    private string _foundCountText = string.Empty;
    public string FoundCountText
    {
        get => _foundCountText;
        private set => SetField(ref _foundCountText, value);
    }

    private string _noResultsText = string.Empty;
    public string NoResultsText
    {
        get => _noResultsText;
        private set => SetField(ref _noResultsText, value);
    }

    public ObservableCollection<RecentSearchItem> RecentSearches { get; } = [];
    public ObservableCollection<FilterPillItem> FilterPills { get; } = [];
    public ObservableCollection<CategoryTabItem> CategoryTabs { get; } = [];
    public FastObservableCollection<SearchResultItem> SearchResultItems { get; } = [];

    public bool HasRecentSearches => RecentSearches.Count > 0;
    public bool HasNoRecentSearches => RecentSearches.Count == 0;
    public bool HasResults => SearchResultItems.Count > 0;
    public bool HasNoResults => IsInSearchResultsMode && !IsSearching && SearchResultItems.Count == 0;

    public SearchViewModel(ISmsService? smsService = null, IDateFormattingService? dateFormatter = null)
    {
        _smsService = smsService ?? IPlatformApplication.Current?.Services.GetService<ISmsService>();
        _dateFormatter = dateFormatter ?? IPlatformApplication.Current?.Services.GetService<IDateFormattingService>() ?? new DateFormattingService();

        LoadRecentSearches();
        PopulateDefaultFilterPills();
    }

    private Task? _initializeTask;

    public Task InitializeAsync() => _initializeTask ??= LoadSimFiltersAsync();

    public void AddRecentSearch(string query)
    {
        var trimmed = query?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        var existing = RecentSearches.FirstOrDefault(r => string.Equals(r.Query, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            RecentSearches.Remove(existing);
        }

        RecentSearches.Insert(0, new RecentSearchItem(trimmed));

        while (RecentSearches.Count > 15)
        {
            RecentSearches.RemoveAt(RecentSearches.Count - 1);
        }

        SaveRecentSearches();
        NotifyRecentSearchesChanged();
    }

    public void RemoveRecentSearch(RecentSearchItem item)
    {
        if (item == null)
            return;

        if (RecentSearches.Remove(item))
        {
            SaveRecentSearches();
            NotifyRecentSearchesChanged();
        }
    }

    public void ClearRecentSearches()
    {
        RecentSearches.Clear();
        SaveRecentSearches();
        NotifyRecentSearchesChanged();
    }

    public void ClearSearchText()
    {
        SearchText = string.Empty;
    }

    public void ClearActiveFilter()
    {
        ActiveFilter = null;
        var chatsTab = CategoryTabs.FirstOrDefault(t => t.Kind == CategoryFilterKind.All);
        if (chatsTab != null)
        {
            SelectCategoryTab(chatsTab);
        }
    }

    public void ApplyFilter(FilterPillItem pill)
    {
        if (pill == null)
            return;

        // "this layout is only for normal search, and filters which are not Link, Location. for others."
        if (pill.FilterKind == SearchFilterKind.None && (pill.QueryPrefix == "is:links" || pill.QueryPrefix == "is:places"))
        {
            // Do not switch to this results layout for Links and Location
            SearchText = pill.QueryPrefix;
            return;
        }

        ActiveFilter = pill;
    }

    public void ToggleDeepSearch()
    {
        IsDeepSearchActive = !IsDeepSearchActive;
    }

    public void SelectCategoryTab(CategoryTabItem selectedTab)
    {
        if (selectedTab == null)
            return;

        foreach (var tab in CategoryTabs)
        {
            tab.IsSelected = (tab == selectedTab);
        }

        ApplyCategoryFilter(selectedTab);
    }

    private void ApplyCategoryFilter(CategoryTabItem tab)
    {
        IEnumerable<SearchResultItem> filtered = tab.Kind switch
        {
            CategoryFilterKind.All => _allSearchResults,
            CategoryFilterKind.Unread => _allSearchResults.Where(r => r.IsUnread),
            CategoryFilterKind.Starred => _allSearchResults.Where(r => r.IsStarred),
            CategoryFilterKind.Known => _allSearchResults.Where(r => r.IsKnown),
            CategoryFilterKind.Unknown => _allSearchResults.Where(r => !r.IsKnown),
            CategoryFilterKind.Sim when tab.SimSlot.HasValue => _allSearchResults.Where(r => r.SimSlot == tab.SimSlot.Value),
            _ => _allSearchResults
        };

        SearchResultItems.Reset(filtered);

        var loc = Localization.LocalizationManager.Instance;
        FoundCountText = string.Format(loc["Search_Found"] ?? "{0} found", SearchResultItems.Count);

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private void TriggerDebouncedSearch()
    {
        // Cancel() on an already-disposed source throws ObjectDisposedException, so the field
        // must never keep pointing at a disposed instance (clearing a filter, then picking
        // another one, used to crash here).
        var previous = _searchCts;
        _searchCts = null;
        if (previous != null)
        {
            try { previous.Cancel(); } catch (ObjectDisposedException) { }
            previous.Dispose();
        }

        if (!IsInSearchResultsMode)
        {
            SearchResultItems.Reset(Enumerable.Empty<SearchResultItem>());
            _allSearchResults.Clear();
            CategoryTabs.Clear();
            IsSearching = false;
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasNoResults));
            return;
        }

        // Flag the search as pending immediately so the "no results" state does not flash
        // during the debounce window.
        IsSearching = true;

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200, token);
                await ExecuteSearchAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                MainThread.BeginInvokeOnMainThread(() => IsSearching = false);
            }
        });
    }

    public async Task ExecuteSearchAsync(CancellationToken cancellationToken = default)
    {
        if (_smsService == null)
            return;

        try
        {
            // SIM slots are needed to label results; do not race the first search against the SIM load.
            await InitializeAsync();
            cancellationToken.ThrowIfCancellationRequested();

            var text = SearchText?.Trim();
            var filterKind = ActiveFilter?.FilterKind ?? SearchFilterKind.None;
            var simSlot = ActiveFilter?.SimSlot;

            var query = new SearchQuery(
                Text: text,
                FilterKind: filterKind,
                SimSlot: simSlot,
                IncludeArchivedAndSpam: IsDeepSearchActive
            );

            var rawResults = await _smsService.SearchChatsAsync(query, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTime.Now;
            var culture = CultureInfo.CurrentUICulture;
            var loc = Localization.LocalizationManager.Instance;

            var items = new List<SearchResultItem>(rawResults.Count);
            foreach (var r in rawResults)
            {
                var displayName = !string.IsNullOrWhiteSpace(r.ContactName)
                    ? r.ContactName
                    : PhoneNumberNormalizer.IsAlphanumeric(r.Address)
                        ? r.Address
                        : PhoneNumberNormalizer.FormatDisplay(r.Address);

                var initials = ThreadItem.GenerateInitials(displayName);
                var slot = _simSlotMap.TryGetValue(r.SubId, out var mappedSlot) ? mappedSlot : 1;
                var badgeColor = slot == 1 ? PrimaryColor : (slot == 2 ? SecondaryColor : TertiaryColor);

                var formattedSnippet = BuildFormattedSnippet(r.Snippet, text);

                items.Add(new SearchResultItem
                {
                    ThreadId = r.ThreadId,
                    MessageId = r.MessageId,
                    Address = r.Address,
                    DisplayName = displayName,
                    Initials = initials,
                    SubId = r.SubId,
                    SimSlot = slot,
                    ShowSimBadge = _isDualSim,
                    SimBadgeColor = badgeColor,
                    Time = _dateFormatter.FormatThreadTime(r.Timestamp, now, culture),
                    IsUnread = !r.IsRead,
                    IsStarred = r.IsStarred,
                    IsKnown = r.IsKnown,
                    TotalMatches = r.TotalMatches,
                    FormattedSnippet = formattedSnippet,
                    IsArchived = r.IsArchived,
                    IsSpam = r.IsSpam
                });
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                // An unhandled exception on the UI thread kills the app; a stale or failed
                // result must never do that.
                if (cancellationToken.IsCancellationRequested)
                    return;

                try
                {
                    _allSearchResults.Clear();
                    _allSearchResults.AddRange(items);

                    // Update category tabs
                    UpdateCategoryTabs(items.Count);

                    var targetKind = ActiveFilter?.FilterKind switch
                    {
                        SearchFilterKind.Unread => CategoryFilterKind.Unread,
                        SearchFilterKind.Starred => CategoryFilterKind.Starred,
                        SearchFilterKind.Known => CategoryFilterKind.Known,
                        SearchFilterKind.Unknown => CategoryFilterKind.Unknown,
                        SearchFilterKind.Sim => CategoryFilterKind.Sim,
                        _ => (CategoryTabs.FirstOrDefault(t => t.IsSelected)?.Kind ?? CategoryFilterKind.All)
                    };
                    var targetSlot = ActiveFilter?.SimSlot;

                    var activeTab = CategoryTabs.FirstOrDefault(t =>
                        t.Kind == targetKind && (!targetSlot.HasValue || t.SimSlot == targetSlot.Value))
                        ?? CategoryTabs.FirstOrDefault();
                    if (activeTab != null)
                    {
                        foreach (var t in CategoryTabs)
                            t.IsSelected = (t == activeTab);
                        ApplyCategoryFilter(activeTab);
                    }
                    else
                    {
                        SearchResultItems.Reset(items);
                        FoundCountText = string.Format(loc["Search_Found"] ?? "{0} found", items.Count);
                    }

                    NoResultsText = loc["Search_NoResults"] ?? "No messages found";
                }
                catch (Exception)
                {
                    SearchResultItems.Reset(Enumerable.Empty<SearchResultItem>());
                }
                finally
                {
                    IsSearching = false;
                    OnPropertyChanged(nameof(HasResults));
                    OnPropertyChanged(nameof(HasNoResults));
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            MainThread.BeginInvokeOnMainThread(() => IsSearching = false);
        }
    }

    private void UpdateCategoryTabs(int totalChatsCount)
    {
        var loc = Localization.LocalizationManager.Instance;
        var previousKind = CategoryTabs.FirstOrDefault(t => t.IsSelected)?.Kind ?? CategoryFilterKind.All;
        var previousSlot = CategoryTabs.FirstOrDefault(t => t.IsSelected)?.SimSlot;

        CategoryTabs.Clear();

        // 1. Chats (All)
        CategoryTabs.Add(new CategoryTabItem
        {
            Title = loc["Search_Chats"] ?? "Chats",
            Kind = CategoryFilterKind.All,
            Count = totalChatsCount,
            ShowBadge = true,
            IsSelected = previousKind == CategoryFilterKind.All
        });

        // 2. Unread
        var unreadCount = _allSearchResults.Count(r => r.IsUnread);
        CategoryTabs.Add(new CategoryTabItem
        {
            Title = loc["Search_Filter_Unread"] ?? "Unread",
            Kind = CategoryFilterKind.Unread,
            Count = unreadCount,
            ShowBadge = true,
            IsSelected = previousKind == CategoryFilterKind.Unread
        });

        // 3. Starred
        var starredCount = _allSearchResults.Count(r => r.IsStarred);
        CategoryTabs.Add(new CategoryTabItem
        {
            Title = loc["Search_Filter_Starred"] ?? "Starred",
            Kind = CategoryFilterKind.Starred,
            Count = starredCount,
            ShowBadge = true,
            IsSelected = previousKind == CategoryFilterKind.Starred
        });

        // 4. Known
        var knownCount = _allSearchResults.Count(r => r.IsKnown);
        CategoryTabs.Add(new CategoryTabItem
        {
            Title = loc["Search_Filter_Known"] ?? "Known",
            Kind = CategoryFilterKind.Known,
            Count = knownCount,
            ShowBadge = true,
            IsSelected = previousKind == CategoryFilterKind.Known
        });

        // 5. Unknown
        var unknownCount = _allSearchResults.Count(r => !r.IsKnown);
        CategoryTabs.Add(new CategoryTabItem
        {
            Title = loc["Search_Filter_Unknown"] ?? "Unknown",
            Kind = CategoryFilterKind.Unknown,
            Count = unknownCount,
            ShowBadge = true,
            IsSelected = previousKind == CategoryFilterKind.Unknown
        });

        // 6. Dynamic SIM tabs based on detected active SIMs (never hardcoded)
        if (_activeSims != null)
        {
            foreach (var sim in _activeSims)
            {
                var slotNumber = sim.SlotIndex;
                var simCount = _allSearchResults.Count(r => r.SimSlot == slotNumber);
                var displayName = !string.IsNullOrWhiteSpace(sim.DisplayName)
                    ? (_isDualSim ? $"SIM {slotNumber}: {sim.DisplayName}" : sim.DisplayName)
                    : $"SIM {slotNumber}";

                CategoryTabs.Add(new CategoryTabItem
                {
                    Title = displayName,
                    Kind = CategoryFilterKind.Sim,
                    SimSlot = slotNumber,
                    Count = simCount,
                    ShowBadge = true,
                    IsSelected = previousKind == CategoryFilterKind.Sim && previousSlot == slotNumber
                });
            }
        }

        if (!CategoryTabs.Any(t => t.IsSelected))
        {
            var first = CategoryTabs.FirstOrDefault();
            if (first != null) first.IsSelected = true;
        }
    }

    public static FormattedString BuildFormattedSnippet(string snippet, string? queryText)
    {
        var fs = new FormattedString();
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var normalColor = isDark ? Color.FromArgb("#9AA0AB") : Color.FromArgb("#59615A");
        var highlightText = isDark ? Color.FromArgb("#FAD998") : Color.FromArgb("#946300");

        if (string.IsNullOrWhiteSpace(snippet))
        {
            return fs;
        }

        var cleanSnippet = snippet.Replace('\r', ' ').Replace('\n', ' ').Trim();

        if (string.IsNullOrWhiteSpace(queryText))
        {
            var truncated = cleanSnippet.Length > 60 ? cleanSnippet.Substring(0, 60) + "..." : cleanSnippet;
            fs.Spans.Add(new Span
            {
                Text = truncated,
                TextColor = normalColor,
                FontSize = 13
            });
            return fs;
        }

        var cleanQuery = queryText.Trim();
        var idx = cleanSnippet.IndexOf(cleanQuery, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            var truncated = cleanSnippet.Length > 60 ? cleanSnippet.Substring(0, 60) + "..." : cleanSnippet;
            fs.Spans.Add(new Span
            {
                Text = truncated,
                TextColor = normalColor,
                FontSize = 13
            });
            return fs;
        }

        var start = Math.Max(0, idx - 18);
        if (start > 0 && char.IsLowSurrogate(cleanSnippet[start]))
            start--;

        var prefixLen = Math.Max(0, idx - start);
        var prefix = (start > 0 ? "..." : "") + (prefixLen > 0 ? cleanSnippet.Substring(start, prefixLen) : string.Empty);

        var matchLen = Math.Min(cleanQuery.Length, cleanSnippet.Length - idx);
        var match = cleanSnippet.Substring(idx, matchLen);

        var suffixStart = idx + matchLen;
        var maxSuffixLen = Math.Max(0, Math.Min(cleanSnippet.Length - suffixStart, 35));
        if (suffixStart + maxSuffixLen < cleanSnippet.Length && char.IsHighSurrogate(cleanSnippet[suffixStart + maxSuffixLen - 1]))
            maxSuffixLen--;

        var suffix = (maxSuffixLen > 0 ? cleanSnippet.Substring(suffixStart, maxSuffixLen) : string.Empty) +
                     (suffixStart + maxSuffixLen < cleanSnippet.Length ? "..." : "");

        if (!string.IsNullOrEmpty(prefix))
        {
            fs.Spans.Add(new Span
            {
                Text = prefix,
                TextColor = normalColor,
                FontSize = 13
            });
        }

        fs.Spans.Add(new Span
        {
            Text = match,
            TextColor = highlightText,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            FontFamily = ThreadItem.FontFamilyBold
        });

        if (!string.IsNullOrEmpty(suffix))
        {
            fs.Spans.Add(new Span
            {
                Text = suffix,
                TextColor = normalColor,
                FontSize = 13
            });
        }

        return fs;
    }

    private static Color PrimaryColor =>
        Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#34D399") : Color.FromArgb("#386948");

    private static Color SecondaryColor =>
        Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#9AA0AB") : Color.FromArgb("#665E53");

    private static Color TertiaryColor =>
        Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#EBCB8B") : Color.FromArgb("#745C27");

    private void PopulateDefaultFilterPills()
    {
        FilterPills.Clear();

        var primaryColor = PrimaryColor;
        var secondaryColor = SecondaryColor;

        var loc = Localization.LocalizationManager.Instance;

        FilterPills.Add(new FilterPillItem
        {
            Title = loc["Search_Filter_Unread"] ?? "Unread",
            QueryPrefix = "is:unread",
            FilterKind = SearchFilterKind.Unread,
            IconSource = "mail.png",
            IconTint = primaryColor
        });

        FilterPills.Add(new FilterPillItem
        {
            Title = loc["Search_Filter_Starred"] ?? "Starred",
            QueryPrefix = "is:starred",
            FilterKind = SearchFilterKind.Starred,
            IconSource = "star.png",
            IconTint = primaryColor
        });

        FilterPills.Add(new FilterPillItem
        {
            Title = loc["Search_Filter_Known"] ?? "Known",
            QueryPrefix = "is:known",
            FilterKind = SearchFilterKind.Known,
            IconSource = "person.png",
            IconTint = secondaryColor
        });

        FilterPills.Add(new FilterPillItem
        {
            Title = loc["Search_Filter_Unknown"] ?? "Unknown",
            QueryPrefix = "is:unknown",
            FilterKind = SearchFilterKind.Unknown,
            IconSource = "shield.png",
            IconTint = secondaryColor
        });

        FilterPills.Add(new FilterPillItem
        {
            Title = loc["Search_Filter_Links"] ?? "Links",
            QueryPrefix = "is:links",
            FilterKind = SearchFilterKind.None,
            IconSource = "link.png",
            IconTint = secondaryColor
        });

        FilterPills.Add(new FilterPillItem
        {
            Title = loc["Search_Filter_Places"] ?? "Places",
            QueryPrefix = "is:places",
            FilterKind = SearchFilterKind.None,
            IconSource = "location_on.png",
            IconTint = secondaryColor
        });
    }

    private async Task LoadSimFiltersAsync()
    {
        if (_smsService == null)
            return;

        try
        {
            var activeSims = await _smsService.GetActiveSimsAsync();
            _activeSims = activeSims ?? Array.Empty<SimCardInfo>();
            _isDualSim = await _smsService.IsDualSimAsync();
            _simSlotMap = await _smsService.GetSimSlotMapAsync();

            var simColors = new[]
            {
                PrimaryColor,
                TertiaryColor,
                SecondaryColor
            };

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var existingSims = FilterPills.Where(p => p.QueryPrefix.StartsWith("sim:")).ToList();
                foreach (var sim in existingSims)
                {
                    FilterPills.Remove(sim);
                }

                if (activeSims == null || activeSims.Count == 0)
                    return;

                var insertIndex = FilterPills.TakeWhile(p => p.QueryPrefix != "is:links").Count();

                for (var i = 0; i < activeSims.Count; i++)
                {
                    var sim = activeSims[i];
                    var slotNumber = sim.SlotIndex;
                    var displayName = !string.IsNullOrWhiteSpace(sim.DisplayName)
                        ? (_isDualSim ? $"SIM {slotNumber}: {sim.DisplayName}" : sim.DisplayName)
                        : $"SIM {slotNumber}";

                    var pill = new FilterPillItem
                    {
                        Title = displayName,
                        QueryPrefix = $"sim:{slotNumber}",
                        FilterKind = SearchFilterKind.Sim,
                        SimSlot = slotNumber,
                        IconSource = "sim_card.png",
                        IconTint = simColors[i % simColors.Length]
                    };

                    FilterPills.Insert(insertIndex + i, pill);
                }
            });
        }
        catch
        {
        }
    }

    private void LoadRecentSearches()
    {
        RecentSearches.Clear();

        var json = Preferences.Default.Get<string?>(RecentSearchesKey, null);
        if (json is not null)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                            RecentSearches.Add(new RecentSearchItem(item));
                    }
                }
            }
            catch
            {
            }
        }

        NotifyRecentSearchesChanged();
    }

    private void SaveRecentSearches()
    {
        try
        {
            var list = RecentSearches.Select(r => r.Query).ToList();
            var json = JsonSerializer.Serialize(list);
            Preferences.Default.Set(RecentSearchesKey, json);
        }
        catch
        {
        }
    }

    private void NotifyRecentSearchesChanged()
    {
        OnPropertyChanged(nameof(HasRecentSearches));
        OnPropertyChanged(nameof(HasNoRecentSearches));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
