using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.ViewModels;
using Gheychi.Core.Notifications;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;
using Gheychi.Core.Updates;
using Gheychi.Infrastructure.Data;
using Gheychi.Infrastructure.Spam;
using Gheychi.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace Gheychi.App;

internal static class AppServices
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "gheychi.db");

        services.AddSingleton<IDateFormattingService, DateFormattingService>();
        services.AddSingleton<IMessageMetadataRepository>(_ => new MessageMetadataRepository(databasePath));
        services.AddSingleton<ISmsService, AndroidSmsService>();

        services.AddSingleton<ISpamSettings, Services.PreferencesSpamSettings>();
        services.AddSingleton<ITrustedSenders, Services.PreferencesTrustedSenders>();
        services.AddSingleton<IBlockedSenders, Services.PreferencesBlockedSenders>();
        services.AddSingleton<ISpamMessageRepository>(_ => new SpamMessageRepository(databasePath));
        services.AddSingleton<ISpamStatsRepository>(_ => new SpamStatRepository(databasePath));
        services.AddSingleton(_ => new SpamModelStore(
            Path.Combine(FileSystem.AppDataDirectory, "spam-model"),
            name => FileSystem.OpenAppPackageFileAsync($"SpamModel/{name}")));
        services.AddSingleton<MlNetSpamClassifier>();
        services.AddSingleton<ISpamClassifier>(sp => sp.GetRequiredService<MlNetSpamClassifier>());
        services.AddSingleton<ISpamModelUpdater>(sp => sp.GetRequiredService<MlNetSpamClassifier>());
        services.AddSingleton<ISpamModelSource>(_ => new GitHubSpamModelSource(
            new HttpClient { Timeout = TimeSpan.FromMinutes(2) },
            FileSystem.CacheDirectory));
        services.AddSingleton<IAppReleaseSource>(_ => new GitHubAppReleaseSource(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            GitHubAppReleaseSource.ChooseAbi(global::Android.OS.Build.SupportedAbis ?? [])));
        services.AddSingleton<IAppUpdateState, Services.PreferencesAppUpdateState>();
        services.AddSingleton(sp => new AppUpdates(
            sp.GetRequiredService<IAppReleaseSource>(),
            sp.GetRequiredService<IAppUpdateState>(),
            AppInfo.Current.VersionString));
        services.AddSingleton<ISpamReporter>(_ => new GoogleFormSpamReporter(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));
        services.AddSingleton<ISpamModelUpdateState, Services.PreferencesSpamModelUpdateState>();
        services.AddSingleton(sp => new SpamModelUpdates(
            sp.GetRequiredService<ISpamModelSource>(),
            sp.GetRequiredService<ISpamModelUpdater>(),
            sp.GetRequiredService<ISpamModelUpdateState>()));
        services.AddSingleton<SpamDetector>();
        services.AddSingleton<SpamViewModel>();

        // A new reason to hold back a notification (archived, snoozed) is one more INotificationRule here.
        // Spam never gets this far: it is quarantined before the message is stored.
        services.AddSingleton<ActiveChatState>();
        services.AddSingleton<IActiveChatState>(sp => sp.GetRequiredService<ActiveChatState>());
        services.AddSingleton<IThreadSettings, Services.PreferencesThreadSettings>();
        services.AddSingleton<INotificationRule, ActiveChatRule>();
        services.AddSingleton<INotificationRule, SnoozeRule>();
        services.AddSingleton<NotificationPolicy>();
        services.AddSingleton<MessagesViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<SearchViewModel>();

        return services.BuildServiceProvider();
    }
}
