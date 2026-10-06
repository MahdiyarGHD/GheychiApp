using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Gheychi.App.ViewModels;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private static readonly CornerRadius OutgoingCornersLtr = new(18, 18, 4, 18);
    private static readonly CornerRadius IncomingCornersLtr = new(18, 18, 18, 4);
    private static readonly CornerRadius OutgoingCornersRtl = new(18, 18, 18, 4);
    private static readonly CornerRadius IncomingCornersRtl = new(18, 18, 4, 18);

    private static readonly Color IncomingBgDark = Color.FromArgb("#1C1F24");
    private static readonly Color IncomingBgLight = Color.FromArgb("#EDF1EB");
    private static readonly Color TextDark = Color.FromArgb("#E8EAED");
    private static readonly Color TextLight = Color.FromArgb("#1B1E24");
    private static readonly Color TimeColorNormal = Color.FromArgb("#8A8F98");
    private static readonly Color PrimaryColor = Color.FromArgb("#2E6B4C");
    private static readonly Color DangerColor = Color.FromArgb("#D64545");
    private static readonly Brush UncheckedStrokeLight = new SolidColorBrush(Color.FromArgb("#9AA0AB"));
    private static readonly Brush UncheckedStrokeDark = new SolidColorBrush(Color.FromArgb("#5C6370"));

    private static bool IsDarkTheme => Application.Current?.RequestedTheme == AppTheme.Dark;

    private static readonly Color[] LightBgColors =
    [
        Color.FromArgb("#E6F4F1"), // Teal
        Color.FromArgb("#FEF3C7"), // Amber
        Color.FromArgb("#EEF2FF"), // Indigo
        Color.FromArgb("#F5F3FF"), // Purple
    ];

    private static readonly Color[] DarkBgColors =
    [
        Color.FromArgb("#142B28"),
        Color.FromArgb("#2E2010"),
        Color.FromArgb("#1E1B4B"),
        Color.FromArgb("#2E1065"),
    ];

    private static readonly Color[] LightTextColors =
    [
        Color.FromArgb("#0F766E"),
        Color.FromArgb("#B45309"),
        Color.FromArgb("#4338CA"),
        Color.FromArgb("#7C3AED"),
    ];

    private static readonly Color[] DarkTextColors =
    [
        Color.FromArgb("#5EEAD4"),
        Color.FromArgb("#FCD34D"),
        Color.FromArgb("#A5B4FC"),
        Color.FromArgb("#C4B5FD"),
    ];

    // Soft tint + dark text per SIM slot (light theme) or deep tint + light text (dark theme):
    // always readable, unlike white text on a pastel fill.
    public static Color SimTintBackground(int slot) =>
        (IsDarkTheme ? DarkBgColors : LightBgColors)[Math.Max(0, (slot - 1) % LightBgColors.Length)];

    public static Color SimTintText(int slot) =>
        (IsDarkTheme ? DarkTextColors : LightTextColors)[Math.Max(0, (slot - 1) % LightTextColors.Length)];

    public static CornerRadius IncomingCorners =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? IncomingCornersRtl : IncomingCornersLtr;

    public static CornerRadius OutgoingCorners =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? OutgoingCornersRtl : OutgoingCornersLtr;

    public static Color IncomingBubbleBg => IsDarkTheme ? IncomingBgDark : IncomingBgLight;
    public static Color MessageTextColor => IsDarkTheme ? TextDark : TextLight;
    public static Color MessageTimeColor => TimeColorNormal;

    public long Id { get; set; }
    public DateTime Timestamp { get; init; }

    public required string BodyBeforeLink { get; init; }
    public string Link { get; init; } = string.Empty;
    public string FullBody => string.IsNullOrEmpty(Link) ? BodyBeforeLink : $"{BodyBeforeLink}{Link}";
    public bool IsOutgoing { get; init; }
    public bool IsNotOutgoing => !IsOutgoing;
    public required string Time { get; init; }
    public bool HasMedia { get; init; }
    public bool HasLink => !string.IsNullOrEmpty(Link);
    public bool HasNoLink => string.IsNullOrEmpty(Link);
    public bool IsUnread { get; set; }

    private bool _isStarred;
    public bool IsStarred
    {
        get => _isStarred;
        set => SetField(ref _isStarred, value);
    }

    private string? _reactionEmoji;
    public string? ReactionEmoji
    {
        get => _reactionEmoji;
        set
        {
            if (SetField(ref _reactionEmoji, value))
            {
                OnPropertyChanged(nameof(HasReaction));
                OnPropertyChanged(nameof(ReactionText));
                OnPropertyChanged(nameof(BubblePadding));
                OnPropertyChanged(nameof(RowSpacingForReaction));
            }
        }
    }

    public bool HasReaction => !string.IsNullOrWhiteSpace(_reactionEmoji);

    public Thickness BubblePadding => HasReaction ? new Thickness(10, 7, 10, 16) : new Thickness(10, 7, 10, 7);
    public double RowSpacingForReaction => HasReaction ? 10 : 2;

    // The pill used to be a stack of emoji, spinner and "!" labels; one label is much cheaper to create
    // for every message row, and the pill is only a few characters anyway.
    public string ReactionText => _isReactionSending
        ? $"{_reactionEmoji} …"
        : _hasReactionFailed ? $"{_reactionEmoji} !" : _reactionEmoji ?? string.Empty;

    public Color ReactionTextColor => _hasReactionFailed ? DangerColor : (IsDarkTheme ? TextDark : TextLight);

    private bool _isReactionSending;
    public bool IsReactionSending
    {
        get => _isReactionSending;
        set
        {
            if (SetField(ref _isReactionSending, value))
                OnPropertyChanged(nameof(ReactionText));
        }
    }

    private bool _hasReactionFailed;
    public bool HasReactionFailed
    {
        get => _hasReactionFailed;
        set
        {
            if (SetField(ref _hasReactionFailed, value))
            {
                OnPropertyChanged(nameof(ReactionText));
                OnPropertyChanged(nameof(ReactionTextColor));
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SelectionFill));
                OnPropertyChanged(nameof(SelectionStroke));
                OnPropertyChanged(nameof(SelectionStrokeThickness));
            }
        }
    }

    public Color SelectionFill => _isSelected ? PrimaryColor : Colors.Transparent;
    public Brush SelectionStroke => _isSelected ? Brush.Transparent : (IsDarkTheme ? UncheckedStrokeDark : UncheckedStrokeLight);
    public double SelectionStrokeThickness => _isSelected ? 0 : 1.5;

    private bool _isSelectionMode;
    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        set => SetField(ref _isSelectionMode, value);
    }

    private static readonly Brush MatchStroke = new SolidColorBrush(Color.FromArgb("#F2B84B"));
    private const double MatchStrokeThickness = 1.5;
    private const double CurrentStrokeThickness = 3;

    private SearchMark _searchMark;

    /// <summary>How the open in-chat search sees this message; the bubble's outline follows it.</summary>
    public SearchMark SearchMark
    {
        get => _searchMark;
        set
        {
            if (_searchMark == value)
                return;

            _searchMark = value;
            OnPropertyChanged(nameof(SearchStroke));
            OnPropertyChanged(nameof(SearchStrokeThickness));
        }
    }

    public Brush SearchStroke => _searchMark == SearchMark.None ? Brush.Transparent : MatchStroke;
    public double SearchStrokeThickness => _searchMark switch
    {
        SearchMark.Current => CurrentStrokeThickness,
        SearchMark.Match => MatchStrokeThickness,
        _ => 0
    };

    public int SubId { get; init; }

    private string? _carrierName;
    public string? CarrierName
    {
        get => _carrierName;
        set
        {
            if (SetField(ref _carrierName, value))
            {
                OnPropertyChanged(nameof(SimTagText));
                OnPropertyChanged(nameof(SimTagVisible));
            }
        }
    }

    private int _simSlot = 1;
    public int SimSlot
    {
        get => _simSlot;
        set
        {
            if (SetField(ref _simSlot, value))
            {
                OnPropertyChanged(nameof(SimTagVisible));
                OnPropertyChanged(nameof(SimTagBgColor));
                OnPropertyChanged(nameof(SimTagTextColor));
            }
        }
    }

    private bool _isDualSim;
    public bool IsDualSim
    {
        get => _isDualSim;
        set
        {
            if (SetField(ref _isDualSim, value))
                OnPropertyChanged(nameof(SimTagVisible));
        }
    }

    private bool _isLastMessage;
    public bool IsLastMessage
    {
        get => _isLastMessage;
        set
        {
            if (SetField(ref _isLastMessage, value))
                OnPropertyChanged(nameof(SimTagVisible));
        }
    }

    private bool _isTagRevealed;
    public bool IsTagRevealed
    {
        get => _isTagRevealed;
        set
        {
            if (SetField(ref _isTagRevealed, value))
                OnPropertyChanged(nameof(SimTagVisible));
        }
    }

    public bool SimTagVisible => !IsOutgoing && (_isTagRevealed || (_isLastMessage && IsDualSim && !string.IsNullOrWhiteSpace(CarrierName)));

    public string SimTagText => !string.IsNullOrWhiteSpace(CarrierName) ? CarrierName : $"SIM {SimSlot}";

    public Color SimTagBgColor => (IsDarkTheme ? DarkBgColors : LightBgColors)[Math.Max(0, (SimSlot - 1) % LightBgColors.Length)];

    public Color SimTagTextColor => (IsDarkTheme ? DarkTextColors : LightTextColors)[Math.Max(0, (SimSlot - 1) % LightTextColors.Length)];

    public void ToggleRevealed()
    {
        IsTagRevealed = !IsTagRevealed;
    }

    public CornerRadius BubbleCorners => IsOutgoing ? OutgoingCorners : IncomingCorners;

    private bool _isDelivered;
    public bool IsDelivered
    {
        get => _isDelivered;
        set
        {
            if (SetField(ref _isDelivered, value))
            {
                OnPropertyChanged(nameof(IsSending));
                OnPropertyChanged(nameof(StatusGlyph));
            }
        }
    }

    private bool _hasFailed;
    public bool HasFailed
    {
        get => _hasFailed;
        set
        {
            if (SetField(ref _hasFailed, value))
            {
                OnPropertyChanged(nameof(IsSending));
                OnPropertyChanged(nameof(StatusGlyph));
            }
        }
    }

    private bool _isSent;
    /// <summary>The carrier accepted the message; <see cref="IsDelivered"/> follows when a delivery report confirms it.</summary>
    public bool IsSent
    {
        get => _isSent;
        set
        {
            if (SetField(ref _isSent, value))
            {
                OnPropertyChanged(nameof(IsSending));
                OnPropertyChanged(nameof(StatusGlyph));
            }
        }
    }

    public bool IsSending => IsOutgoing && !IsSent && !IsDelivered && !HasFailed;

    // One tick once the carrier has it, two once it reached the phone; sending shows a spinner and failure the retry row.
    public string StatusGlyph =>
        !IsOutgoing || HasFailed ? string.Empty
        : IsDelivered ? "✓✓"
        : IsSent ? "✓"
        : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
