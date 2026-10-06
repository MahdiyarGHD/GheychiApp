namespace Gheychi.App.ViewModels;

public sealed record SimSettingsRow(string Title, string Subtitle);

public sealed record TrustedSenderRow(string Key, string Display);

public sealed record LicenseRow(string Name, string License, string Url);

public sealed record TopSenderRow(string Display, string Count);
