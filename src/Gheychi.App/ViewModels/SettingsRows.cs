using Avalonia.Media;
using Gheychi.App.Theming;

namespace Gheychi.App.ViewModels;

public sealed record SimSettingsRow(string Title, string Subtitle);

public sealed record TrustedSenderRow(string Key, string Display);

/// <summary>A blocked sender: the contact's name over the number, or the number alone.</summary>
public sealed record BlockedSenderRow(string Key, string Title, string Subtitle)
{
    public bool HasSubtitle => Subtitle.Length > 0;
    public string Initials => ThreadItem.GenerateInitials(Title);
    public bool HasNoInitials => Initials.Length == 0;
}

/// <summary>Messages deleted together: the sender over the newest text, and how many there are and how long they stay.</summary>
public sealed record TrashRow(Gheychi.Core.Services.TrashGroup Group, string Title, string Preview, string Meta)
{
    private (IBrush Bg, IBrush Fg) AvatarColors => AvatarPalette.For(Group.Address, Title);

    public string Initials => ThreadItem.GenerateInitials(Title);
    public bool HasNoInitials => Initials.Length == 0;
    public IBrush AvatarBgColor => AvatarColors.Bg;
    public IBrush AvatarTextColor => AvatarColors.Fg;
}

public sealed record LicenseRow(string Name, string License, string Url);

public sealed record TopSenderRow(string Display, string Count);
