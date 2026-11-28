namespace Gheychi.App;

public static class AppBuildFeatures
{
#if GOOGLE_PLAY
    public const bool FeedbackAndSpamReportingEnabled = false;
#else
    public const bool FeedbackAndSpamReportingEnabled = true;
#endif
}
