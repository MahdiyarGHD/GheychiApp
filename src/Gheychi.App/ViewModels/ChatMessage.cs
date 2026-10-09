using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Gheychi.App.Theming;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

public sealed class ChatMessage : INotifyPropertyChanged
{
    // Avalonia orders corners top-left, top-right, bottom-right, bottom-left (MAUI: ..., bottom-left, bottom-right).
    private static readonly CornerRadius OutgoingCornersLtr = new(18, 18, 18, 4);
    private static readonly CornerRadius IncomingCornersLtr = new(18, 18, 4, 18);
    private static readonly CornerRadius OutgoingCornersRtl = new(18, 18, 4, 18);
    private static readonly CornerRadius IncomingCornersRtl = new(18, 18, 18, 4);

    private static readonly IBrush IncomingBg = Palette.Pick("#DFE3E8", "#1C1F24");
    private static readonly IBrush TextBrush = Palette.Pick("#1B1E24", "#E8EAED");
    private static readonly IBrush TimeBrush = Palette.Brush("#8A8F98");
    private static readonly IBrush PrimaryBrush = Palette.Accent(AccentRole.Solid);
    private static readonly IBrush DangerBrush = Palette.Brush("#D64545");
    private static readonly IBrush UncheckedStroke = Palette.Pick("#9AA0AB", "#5C6370");

    // Soft tint + dark text per SIM slot (light theme) or deep tint + light text (dark theme):
    // always readable, unlike white text on a pastel fill.
    private static readonly IBrush[] SimBackgrounds =
    [
        Palette.Pick("#E6F4F1", "#142B28"), // Teal
        Palette.Pick("#FEF3C7", "#2E2010"), // Amber
        Palette.Pick("#EEF2FF", "#1E1B4B"), // Indigo
        Palette.Pick("#F5F3FF", "#2E1065"), // Purple
    ];

    private static readonly IBrush[] SimTexts =
    [
        Palette.Pick("#0F766E", "#5EEAD4"),
        Palette.Pick("#B45309", "#FCD34D"),
        Palette.Pick("#4338CA", "#A5B4FC"),
        Palette.Pick("#7C3AED", "#C4B5FD"),
    ];

    public static IBrush SimTintBackground(int slot) => SimBackgrounds[Math.Max(0, (slot - 1) % SimBackgrounds.Length)];

    public static IBrush SimTintText(int slot) => SimTexts[Math.Max(0, (slot - 1) % SimTexts.Length)];

    public static CornerRadius IncomingCorners =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? IncomingCornersRtl : IncomingCornersLtr;

    public static CornerRadius OutgoingCorners =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? OutgoingCornersRtl : OutgoingCornersLtr;

    public static IBrush IncomingBubbleBg => IncomingBg;
    public static IBrush MessageTextColor => TextBrush;
    public static IBrush MessageTimeColor => TimeBrush;

    public long Id { get; set; }
    public DateTime Timestamp { get; init; }

    public required string Body { get; init; }

    /// <summary>The links and phone numbers in <see cref="Body"/>; null for the many messages that have none.</summary>
    public LinkSpan[]? Links { get; init; }

    /// <summary>The first web address in the message, as it is written.</summary>
    public string Link
    {
        get
        {
            foreach (var span in Links ?? [])
            {
                if (span.Kind == LinkKind.Url)
                    return Body.Substring(span.Start, span.Length);
            }

            return string.Empty;
        }
    }
    public bool IsOutgoing { get; init; }
    public bool IsNotOutgoing => !IsOutgoing;
    public required string Time { get; init; }
    public bool HasMedia { get; init; }
    public bool HasLink => Links is not null;
    public bool HasUrl => Link.Length > 0;
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

    public IBrush ReactionTextColor => _hasReactionFailed ? DangerBrush : TextBrush;

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

    public IBrush SelectionFill => _isSelected ? PrimaryBrush : Palette.Transparent;
    public IBrush SelectionStroke => _isSelected ? Palette.Transparent : UncheckedStroke;
    public double SelectionStrokeThickness => _isSelected ? 0 : 1.5;

    private bool _isSelectionMode;
    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        set => SetField(ref _isSelectionMode, value);
    }

    private static readonly IBrush MatchStroke = Palette.Brush("#F2B84B");
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

    public IBrush SearchStroke => _searchMark == SearchMark.None ? Palette.Transparent : MatchStroke;
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

    public IBrush SimTagBgColor => SimTintBackground(SimSlot);

    public IBrush SimTagTextColor => SimTintText(SimSlot);

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
                OnPropertyChanged(nameof(HasTick));
                OnPropertyChanged(nameof(HasDoubleTick));
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
                OnPropertyChanged(nameof(HasTick));
                OnPropertyChanged(nameof(HasDoubleTick));
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
                OnPropertyChanged(nameof(HasTick));
                OnPropertyChanged(nameof(HasDoubleTick));
            }
        }
    }

    public bool IsSending => IsOutgoing && !IsSent && !IsDelivered && !HasFailed;

    // One tick once the carrier has it, two once it reached the phone; sending shows a spinner and failure the retry row.
    public bool HasTick => IsOutgoing && !HasFailed && (IsSent || IsDelivered);

    public bool HasDoubleTick => IsOutgoing && !HasFailed && IsDelivered;

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
