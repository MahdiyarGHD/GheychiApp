using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Gheychi.App.ViewModels;

public sealed class ThreadItem : INotifyPropertyChanged
{
    private static readonly Color DangerColor = Color.FromArgb("#D64545");
    private static readonly Color TimeNormalColor = Color.FromArgb("#8A8F98");
    private static readonly Color PreviewDarkColor = Color.FromArgb("#9AA0AB");
    private static readonly Color PreviewLightColor = Color.FromArgb("#5C6370");
    private static readonly Color AvatarBgDarkColor = Color.FromArgb("#35423C");
    private static readonly Color AvatarBgLightColor = Color.FromArgb("#E3E9E4");
    private static readonly Color AvatarTextDarkColor = Color.FromArgb("#8FE0BE");
    private static readonly Color AvatarTextLightColor = Color.FromArgb("#1B5E43");
    private static readonly Color IconTintDark = Color.FromArgb("#D9E3DD");
    private static readonly Color IconTintLight = Color.FromArgb("#33443C");

    private static readonly string CachedFontFamilyBold =
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase)
            ? "VazirmatnSemiBold"
            : "PlusJakartaSansSemiBold";

    private static bool IsDarkTheme => Application.Current?.RequestedTheme == AppTheme.Dark;

    public static string FontFamilyBold => CachedFontFamilyBold;
    public static Color IconTintColor => IsDarkTheme ? IconTintDark : IconTintLight;

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
        }
    }

    public string CountText => Count > 0 ? Count.ToString() : string.Empty;
    public Thickness PreviewMargin => HasFailed ? new Thickness(20, 0, 0, 0) : new Thickness(0);
    public bool HasIcon => !string.IsNullOrEmpty(IconFile);
    public bool HasNoIcon => string.IsNullOrEmpty(IconFile);
    public bool IsNotFailed => !HasFailed;

    public Color TimeColor => HasFailed ? DangerColor : TimeNormalColor;
    public Color PreviewColor => HasFailed ? DangerColor : (IsDarkTheme ? PreviewDarkColor : PreviewLightColor);
    public Color AvatarBgColor => IsDarkTheme ? AvatarBgDarkColor : AvatarBgLightColor;
    public Color AvatarTextColor => IsDarkTheme ? AvatarTextDarkColor : AvatarTextLightColor;

    public void MarkAsRead()
    {
        IsUnread = false;
        Count = 0;
    }

    public static ThreadItem Empty { get; } = new()
    {
        Name = string.Empty,
        Initials = string.Empty,
        Time = string.Empty,
        Preview = string.Empty
    };

    public static string GenerateInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var first = char.ToUpperInvariant(parts[0][0]);
            var second = char.ToUpperInvariant(parts[1][0]);
            return $"{first}{second}";
        }

        if (name.Length <= 2)
            return name.ToUpperInvariant();

        return char.ToUpperInvariant(name[0]).ToString();
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

        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
