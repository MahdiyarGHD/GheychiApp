namespace Gheychi.Core.Updates;

/// <summary>A published version of the app: its release page and, when known, a direct link to its APK.</summary>
public sealed record AppRelease(string Tag, string PageUrl, string? DownloadUrl = null)
{
    public AppVersion? Version => AppVersion.Parse(Tag);
}
