using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Gheychi.App.Theming;

namespace Gheychi.App.ViewModels;

public sealed class ThreadItem : INotifyPropertyChanged
{
    private static readonly IBrush DangerBrush = Palette.Brush("#D64545");
    private static readonly IBrush TimeNormalBrush = Palette.Brush("#8A8F98");
    private static readonly IBrush PreviewBrush = Palette.Pick("#5E6166", "#9AA0AB");
    private static readonly IBrush NameReadBrush = Palette.Pick("#3B3D40", "#C9CED4");
    private static readonly IBrush NameUnreadBrush = Palette.Pick("#000000", "#FFFFFF");
    private static readonly IBrush PreviewUnreadBrush = Palette.Pick("#1B1E24", "#F1F3F5");
    private static readonly IBrush TimeUnreadBrush = Palette.Pick("#1B1E24", "#F1F3F5");
    private static readonly IBrush AvatarBgBrush = Palette.Accent(AccentRole.Soft);
    private static readonly IBrush AvatarBgSelectedBrush = Palette.Accent(AccentRole.Solid);
    private static readonly IBrush AvatarTextBrush = Palette.Accent(AccentRole.Text);
    private static readonly IBrush IconTintBrush = Palette.Pick("#393B3E", "#DDDEDF");
    private static readonly IBrush SelectedRowBrush = Palette.Pick("#142E6B4C", "#262E6B4C");

    private static readonly Thickness FailedPreviewMargin = new(20, 0, 0, 0);

    public static IBrush IconTintColor => IconTintBrush;

    public long ThreadId { get; init; }
    public int SubId { get; init; }
    public required string Name { get; init; }
    public required string Initials { get; init; }
    public string? IconFile { get; init; }
    public required string Time { get; init; }
    public required string Preview { get; init; }
    public string Phone { get; init; } = string.Empty;
    public bool HasFailed { get; init; }
    public int TotalCount { get; init; }

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountText));
        }
    }

    private bool _isUnread;
    public bool IsUnread
    {
        get => _isUnread;
        set
        {
            if (_isUnread == value) return;
            _isUnread = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameColor));
            OnPropertyChanged(nameof(NameFont));
            OnPropertyChanged(nameof(PreviewColor));
            OnPropertyChanged(nameof(PreviewFont));
            OnPropertyChanged(nameof(TimeColor));
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotSelected));
            OnPropertyChanged(nameof(AvatarBgColor));
            OnPropertyChanged(nameof(ShowIcon));
            OnPropertyChanged(nameof(ShowInitials));
            OnPropertyChanged(nameof(RowBackgroundColor));
        }
    }

    public bool IsNotSelected => !IsSelected;

    // Row visuals as plain bindings: a trigger per row (each with its own theme binding) was the costly way to do this.
    public bool ShowIcon => HasIcon && !_isSelected;
    public bool ShowInitials => HasNoIcon && !_isSelected;
    public IBrush RowBackgroundColor => _isSelected ? SelectedRowBrush : Palette.Transparent;
    public string CountText => Count > 0 ? Count.ToString() : string.Empty;
    public Thickness PreviewMargin => HasFailed ? FailedPreviewMargin : default;
    public bool HasIcon => !string.IsNullOrEmpty(IconFile);
    public bool HasNoIcon => string.IsNullOrEmpty(IconFile);
    public bool IsNotFailed => !HasFailed;

    /// <summary>False for sender names and short codes, which cannot be dialled.</summary>
    public bool CanCall => Gheychi.Core.Services.ThreadProfileActions.CanCall(Gheychi.Core.Services.PhoneNumberNormalizer.ToSendAddress(Phone));

    // Unread threads are brighter and bolder than read ones.
    public IBrush NameColor => _isUnread ? NameUnreadBrush : NameReadBrush;
    public FontFamily NameFont => _isUnread ? AppFonts.Bold : AppFonts.Regular;
    public FontFamily PreviewFont => _isUnread ? AppFonts.Bold : AppFonts.Regular;
    public IBrush TimeColor => HasFailed ? DangerBrush : _isUnread ? TimeUnreadBrush : TimeNormalBrush;
    public IBrush PreviewColor => HasFailed ? DangerBrush : _isUnread ? PreviewUnreadBrush : PreviewBrush;
    public IBrush AvatarBgColor => IsSelected ? AvatarBgSelectedBrush : AvatarBgBrush;
    public IBrush AvatarTextColor => AvatarTextBrush;

    public bool HasSameContent(ThreadItem other) =>
        ThreadId == other.ThreadId &&
        SubId == other.SubId &&
        Name == other.Name &&
        Initials == other.Initials &&
        IconFile == other.IconFile &&
        Time == other.Time &&
        Preview == other.Preview &&
        Phone == other.Phone &&
        HasFailed == other.HasFailed &&
        TotalCount == other.TotalCount &&
        Count == other.Count &&
        IsUnread == other.IsUnread;

    public void MarkAsRead()
    {
        IsUnread = false;
        Count = 0;
    }

    public void MarkAsUnread()
    {
        IsUnread = true;
        if (Count == 0)
            Count = 1;
    }

    public static ThreadItem Empty { get; } = new()
    {
        Name = string.Empty,
        Initials = string.Empty,
        Time = string.Empty,
        Preview = string.Empty
    };

    /// <summary>A row for a conversation known only by its id, name and address, e.g. one opened from a notification before the inbox is loaded.</summary>
    public static ThreadItem ForConversation(long threadId, int subId, string name, string address)
    {
        var phone = Gheychi.Core.Services.PhoneNumberNormalizer.FormatDisplay(address);
        var title = string.IsNullOrWhiteSpace(name) ? phone : name;
        return new ThreadItem
        {
            ThreadId = threadId,
            SubId = Math.Max(0, subId),
            Name = title,
            Phone = phone,
            Initials = GenerateInitials(title),
            IconFile = DetectIcon(title, address),
            Time = string.Empty,
            Preview = string.Empty
        };
    }

    public static string GenerateInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        if (IsNumber(name))
            return string.Empty;

        var trimmed = name.TrimStart();
        foreach (var c in trimmed)
        {
            if (char.IsLetterOrDigit(c))
                return char.ToUpperInvariant(c).ToString();
        }

        return StringInfo.GetNextTextElement(trimmed);
    }

    /// <summary>A name with no letter in it, only digits and the signs of a number or a code (+98 912 000 0003, *123#).</summary>
    public static bool IsNumber(string name)
    {
        var hasDigit = false;
        foreach (var c in name)
        {
            if (char.IsLetter(c))
                return false;
            hasDigit |= char.IsDigit(c);
        }

        return hasDigit || name.TrimStart() is ['+' or '*' or '#', ..];
    }

    public static string? DetectIcon(string name, string address)
    {
        var combined = $"{name} {address}".ToLowerInvariant();
        if (combined.Contains("bank") || combined.Contains("بانک") || combined.Contains("mellat") ||
            combined.Contains("melli") || combined.Contains("saman") || combined.Contains("pasargad") ||
            combined.Contains("parsian") || combined.Contains("tejarat") || combined.Contains("keshavarzi") ||
            combined.Contains("maskan") || combined.Contains("saderat") || combined.Contains("refah"))
            return "bank.png";

        if (combined.Contains("snapp") || combined.Contains("tap30") || combined.Contains("tapsi") ||
            combined.Contains("post") || combined.Contains("پست") || combined.Contains("delivery") ||
            combined.Contains("courier") || combined.Contains("truck") || combined.Contains("فدک"))
            return "truck.png";

        if (combined.Contains("wallex") || combined.Contains("والکس") || combined.Contains("nobitex") ||
            combined.Contains("نوبیتکس") || combined.Contains("bitcoin") || combined.Contains("رمز") ||
            combined.Contains("صرافی"))
            return "bitcoin.png";

        return IsNumber(name) ? "person.png" : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
