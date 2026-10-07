namespace Gheychi.Core.Updates;

/// <summary>A published version of the app and the page it is downloaded from.</summary>
public sealed record AppRelease(string Tag, string PageUrl)
{
    public AppVersion? Version => AppVersion.Parse(Tag);
}
